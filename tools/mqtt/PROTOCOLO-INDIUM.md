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

## Monitoramento de catracas (painel) — FEITO
- Tabela `fp_turnstile_presence` (migração 022): registra toda placa vista no MQTT (device_id,
  status, first/last_seen, firmware, board_id, serial_id, ip_local, media) — independente de cadastro.
- Worker: o `TurnstileMessageCodec.Decode` agora extrai os metadados do `info` (antes descartados);
  o `MqttTurnstileService` faz upsert da presença a cada telemetria/leitura (`ITurnstileMonitoringService`).
- API: `GET /api/turnstiles` (perm `dispositivo.gerenciar`) cruza presença × `fp_devices` (por
  identifier) × portaria/evento; `online` = last_seen dentro de 90s.
- Frontend: tela **Catracas** (grupo Operação) com cards — nome, online/offline, firmware/IP/série,
  e a portaria/evento atribuídos (ou aviso "não atribuída"). Auto-refresh 10s.
- Validado com a placa real: apareceu como online, firmware 1.0.35, ip 192.168.15.9, board neon025156;
  ao cadastrar como device Mqtt (identifier=`Catraca 151`) o painel passou a mostrar portaria/evento.

## ✅ LEITURA DE QR CAPTURADA (04/09) — protocolo descoberto!
Com `accmode:5`, a placa PUBLICA a leitura via MQTT. Capturado:

**Tópico:** `FastPass/<DeviceId>/from/log/5/0`  (ex.: `FastPass/Catraca151/from/log/5/0`)
Formato do tópico parece ser `.../from/log/<accmode>/<algo>`.

**Payload (leitura de QR):**
```json
{"cmd":"log","timestamp":"260904173344","tz":-3,"accmode":5,"origin":5,
 "search":"ref","uid":0,"ref":"7898483340453","event":42,"ol":"0"}
```
Campos:
- `cmd":"log"` → evento de log/leitura
- `ref` → **O CÓDIGO LIDO** (ex.: "7898483340453"). Campo principal.
- `search":"ref"` → buscou por referência
- `accmode":5` → modo de acesso que ATIVA a publicação das leituras (antes era 0/11, não publicava)
- `origin":5` → origem da leitura
- `uid":0` → id na lista local (0 = não encontrado localmente)
- `event":42` → tipo de evento (a confirmar; provável "não encontrado/consulta")
- `ol":"0"` → provável flag online

Observações do `info` no momento: `media:"wireless"`, `iplocal:"192.168.15.7"`,
`reader1active:0` (Wiegand off — QR entra pela UART), `accmode:"5"`.

PENDENTE confirmar:
- Como RESPONDER (liberar/negar) — tópico `to/...` e payload que a placa espera.
- Significado exato de `event`, `origin`, `ol`, e do segundo número do tópico (`/5/0`).
- Como o `accmode` foi para 5 (o usuário mudou algo — documentar).

## Descobertas sobre o modelo de operação da placa (IMPORTANTE)
Investigação em 02/09 revelou que, com o firmware atual (1.0.35), a leitura de QR
NÃO é encaminhada ao servidor por MQTT:
- O leitor de QR entra pela **UART** (`uart1desc:"Data <CR>"`); a placa parece validar
  **localmente** contra a lista de cartões (`capcards:5056`) — por isso só apita e não consulta.
- **MQTT** observado só com telemetria (status/keepalive/info) e reação a comandos `to`.
  Nenhuma leitura de credencial chegou em tópico `from/*` mesmo passando vários QRs.
- **UDP porta 17001** (campo `udp_port` no info) é, segundo o manual, uma **ENTRADA**:
  "outros equipamentos podem ENVIAR informações PARA o controlador (cartões/tags lidos em
  outros dispositivos)". Ou seja, a placa RECEBE por UDP; não envia a validação por ele.
- A placa é **instável no MQTT**: conecta e desconecta sozinha ("connection closed by client").

Conclusão provisória: esta placa/firmware parece desenhada para operação **local/offline**
(valida contra a própria lista). O modo "consulta o servidor a cada leitura" (online) — se existir —
depende de configuração/firmware que ainda não temos documentado. **Sem a doc de integração da
Neon 1.2 não dá para confirmar se há validação online e como ativá-la.**

## Estado do software (nosso lado) — PRONTO e correto
- MQTT do Worker robusto (re-assina ao reconectar, ignora retidas, trata disconnect).
- Painel de catracas mostra presença/online corretamente conforme o heartbeat.
- O pipeline leitura→validação→comando existe e está testado com publish simulado; só falta
  a placa REALMENTE enviar a leitura (que hoje ela não faz por MQTT).

## PENDENTE (depende da Iongrade)
1. Obter a doc de integração da **Neon 1.2**: confirmar se há modo de validação ONLINE
   (consulta ao servidor por leitura) e por qual transporte (MQTT? outro?).
2. Se for MQTT: descobrir o verbo/payload de leitura e de comando → ajustar `TurnstileMessageCodec`.
3. Teste fim-a-fim: QR real → catraca gira (relé LOCK) no sentido correto.
