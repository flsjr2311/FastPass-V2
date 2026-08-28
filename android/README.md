# FastPass Validador — App Android

App de validação de ingressos na portaria. Kotlin + Jetpack Compose.

## O que ele faz
1. **Login** do operador (usuário/senha da mesma API do sistema). Requer a permissão `acesso.validar`.
2. **Seleção de evento e portaria** onde o operador está validando.
3. **Leitura do QR/código** por três formas:
   - Câmera (CameraX + ML Kit)
   - Leitor de código acoplado por **USB-C** (funciona como teclado HID; digita o código e envia Enter)
   - Digitação **manual**
4. **Validação** via `POST /api/access/app/validate`, com feedback **verde (liberado) / vermelho (negado)**, mensagem/motivo, **som e vibração**, e um **feed** das últimas 10 leituras.
5. Cada validação registra a **identificação do aparelho** (nome amigável + id de instalação) na auditoria de acessos do sistema.

## Requisitos para compilar
- **Android Studio** (Koala/2024.1 ou mais novo) com o **Android SDK 34**
- JDK 17 (o Android Studio já inclui um)

> Este repositório não contém o Android SDK. Abra a pasta `android/` no Android Studio; ele baixa o Gradle (via wrapper) e as dependências automaticamente. O `gradle-wrapper.jar` é gerado pelo Android Studio na primeira sincronização (ou rode `gradle wrapper` se tiver o Gradle instalado).

## Configuração do servidor
A URL da API é configurável na tela de login → "Configurar servidor". Padrões:
- **Emulador Android**: `http://10.0.2.2:5088` (acessa o `localhost` da máquina host)
- **Aparelho físico**: use o IP da máquina na rede local, ex.: `http://192.168.0.10:5088`
  - A API precisa estar ouvindo em `0.0.0.0` (já está: `Now listening on: http://0.0.0.0:5088`)
  - Aparelho e máquina na mesma rede; liberar a porta 5088 no firewall se necessário.

O app aceita HTTP puro (cleartext) para facilitar o ambiente de desenvolvimento/rede local.

## Nome do aparelho (auditoria)
Em "Configurações do aparelho" defina um nome (ex.: "Portaria A - Celular 1"). Esse nome aparece na tela de **Auditoria de acessos** do sistema web, junto do selo **📱 App**.

## Estrutura
```
app/src/main/java/com/fastpass/validator/
  MainActivity.kt            # roteamento de telas, permissão de câmera, captura do leitor USB-C, som/vibração
  data/
    AppPreferences.kt        # DataStore: baseUrl, cookie de sessão, deviceInstallId, deviceLabel
    FastPassRepository.kt     # operações de rede + tradução de erros
    model/Dtos.kt            # DTOs (Moshi)
    net/                     # Retrofit, cookie de sessão, provider
  ui/
    MainViewModel.kt         # estado global (MVVM)
    theme/Theme.kt
    scanner/CameraScanner.kt # CameraX + ML Kit
    screens/                 # Login, Setup, Scanner, Settings
```
