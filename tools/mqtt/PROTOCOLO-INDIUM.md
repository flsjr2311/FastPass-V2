# Engenharia reversa — protocolo MQTT da catraca Indium (Iongrade)

Notas da captura de tráfego (engenharia reversa). O manual de hardware descreve
as telas de config, mas NÃO traz o contrato MQTT — descoberto por captura.

## Ambiente de captura (dev)
- Broker: **Mosquitto 2.1.2** instalado no notebook (via winget `EclipseFoundation.Mosquitto`).
  - Serviço do Windows parado e em StartupType=Manual (só ouvia em localhost).
  - Rodando manualmente com `tools/mqtt/mosquitto-dev.conf` (listener `0.0.0.0:1883`, `allow_anonymous true`).
  - Firewall: regra "Mosquitto 1883" liberando TCP 1883.
- Notebook (broker): **192.168.15.166** | Catraca: **192.168.15.9** (mesma LAN 192.168.15.x)
- Captura: `mosquitto_sub -h 127.0.0.1 -p 1883 -t '#' -v` → grava em `tools/mqtt/capture.log`

### Como retomar o broker + captura amanhã
```powershell
# subir o broker (se não estiver rodando)
Start-Process -FilePath "C:\Program Files\mosquitto\mosquitto.exe" `
  -ArgumentList @('-v','-c','tools\mqtt\mosquitto-dev.conf') `
  -RedirectStandardError "tools\mqtt\broker.log" -RedirectStandardOutput "tools\mqtt\broker.out" -WindowStyle Hidden

# subir a captura de todos os tópicos
Start-Process -FilePath "C:\Program Files\mosquitto\mosquitto_sub.exe" `
  -ArgumentList @('-h','127.0.0.1','-p','1883','-t','#','-v') `
  -RedirectStandardOutput "tools\mqtt\capture.log" -RedirectStandardError "tools\mqtt\capture.err" -WindowStyle Hidden
```

## Config aplicada na placa (tela MQTT)
- Conexão: manual/customizado | TLS: off
- Broker: 192.168.15.166 | Port: 1883 | sem user/senha
- Facility ID: `FastPass` | Device ID: `Catraca 151`
- Interface de rede: **wired** (Ethernet), conforme campo `media` do info.

## Estrutura de tópicos (DESCOBERTO)
Padrão: `<FacilityID>/<DeviceID>/from/<evento>` para placa → sistema.
Ex.: `FastPass/Catraca 151/from/keepalive`

- `from` = publicações da placa PARA o sistema.
- Por convenção, o sentido sistema → placa deve ser `.../to/<comando>` (A CONFIRMAR na captura da resposta).

### Eventos observados (placa → sistema)
Todos payload JSON com campo `cmd` e `timestamp` (formato `AAMMDDHHMMSS`, ex. `260901190131` = 2026-09-01 19:01:31) e `tz:-3`.

- **status**: `{"cmd":"status","status":"connected","timestamp":...,"tz":-3,"nrestart":16,"ncmqtt":1}`
- **keepalive** (~a cada 30s): `{"cmd":"keepalive","timestamp":...,"uptime":52,"nrestart":16,"ncmqtt":1,"ping_rearm":0}`
- **info**: `{"cmd":"info","boardid":"neon025156","serialid":"25156","version":"1.0.35","lang":"pt_BR","media":"wired","iplocal":"192.168.15.9","ipgateway":"192.168.15.1","subnetmask":"255.255.255.0","capcards":5056,"freecards":5056,"maxlogs":5056,"reader1active":1,"reader1desc":"Wiegand 26 Std","reader2active":1,...,"uart1desc":"Data <CR>",...,"accmode":"0","udp_port":17001}`
  - `accmode:"0"` — modo de acesso (a entender: provável "sempre consulta servidor").

## PENDENTE (retomar amanhã)
1. **Leitura de QR/cartão** — apresentar credencial na catraca e capturar o evento
   (esperado algo como `.../from/access`, `.../from/card` ou `.../from/online`).
   Objetivos:
   - Nome do sub-tópico da leitura.
   - Campos do payload: código lido, canal/leitor (1=entrada / 2=saída?), direção, formato do código.
   - Se a placa AGUARDA resposta do servidor para liberar (modo online) e QUAL tópico/payload de resposta ela espera (o comando `to` de liberar/negar).
2. Descobrir o **payload de resposta** (autorizar/negar + mensagem no display) — provável `.../to/<algo>` com `cmd` de liberação. Testar publicando manualmente com `mosquitto_pub` e ver a catraca girar.
3. Depois de mapeado: implementar no **FastPass.Worker** um serviço MQTT (MQTTnet) que:
   - Assina `<Facility>/+/from/#`, ao receber leitura chama `IAccessValidationService` (canal Turnstile),
   - Publica a resposta no tópico `to` da placa (liberar/negar + pictograma/mensagem).
   - Cadastro do device: Facility ID + Device ID viram a identidade; mapear para gate/evento no FastPass.

## Decisões já tomadas
- Broker fica no nosso lado (Mosquitto). Placa conecta como cliente.
- Reutilizar a lógica de validação existente (`IAccessValidationService`, canal `Turnstile`,
  que já devolve ArmAction Unlock/KeepLocked e Pictogram).
- A catraca envia apenas {código lido, nome da catraca}. A relação catraca↔portaria e o
  sentido a liberar são decididos pelo servidor (o serviço de validação infere a direção).
- O "nome da catraca" (Device ID MQTT) é gravado no campo `identifier` de `fp_devices`
  (device_type = `Mqtt`), que resolve portaria e evento.

## Fundação implementada (FastPass.Worker) — FEITO
Serviço MQTT hospedado no Worker, pronto para operar assim que o protocolo de leitura/comando
da Neon 1.2 for confirmado. Arquivos em `src/FastPass.Worker/Mqtt/`:
- **MqttOptions** — seção `Mqtt` do appsettings (Host/Port/credenciais/FacilityId/TopicPrefix).
- **TurnstileTopics** — monta/parseia `<Prefix>/<DeviceId>/from|to/<verbo>`; assina `<Prefix>/+/from/#`.
- **TurnstileMessageCodec** — PONTO ÚNICO de tradução do protocolo:
  - Decode: reconhece telemetria (`status`/`keepalive`/`info`); leitura extrai o código de
    campos JSON comuns (code/qrcode/card/...) ou de texto cru terminado em `<CR>`.
  - Encode: monta o comando de resposta (provisório) `{cmd:"access", authorized, release,
    direction, pictogram, message}`. **AJUSTAR quando a doc da Neon 1.2 chegar.**
- **MqttTurnstileService** (BackgroundService) — conecta com reconexão automática; telemetria →
  atualiza `last_seen_at`; leitura → resolve device→portaria→evento (`ResolveTurnstileDeviceAsync`)
  → `IAccessValidationService.ValidateAsync` (canal Turnstile) → publica comando em `.../to/<verbo>`.

Config para ligar/desligar: `Mqtt:Enabled`. Requer fonte NuGet nuget.org (foi adicionada nesta máquina).

### Validado
- Worker compila (0 erros) e conecta no broker; assina `FastPass/+/from/#`.
- Recebe a telemetria real da placa (keepalive/status/info) sem erro.
- Um publish de teste em `.../from/access` foi processado: resolveu device, não achou cadastro
  (esperado — a catraca ainda não foi cadastrada em fp_devices) e logou claramente.

## PENDENTE (quando a doc Neon 1.2 / leitor chegarem)
1. Confirmar o verbo/payload de LEITURA e o de COMANDO → ajustar `TurnstileMessageCodec`.
2. Cadastrar a catraca como device `Mqtt` no FastPass com `identifier` = Device ID MQTT
   (ex.: `Catraca 151`), vinculada a uma portaria de um evento.
3. Teste fim-a-fim: QR real → catraca gira (relé LOCK) no sentido correto.
