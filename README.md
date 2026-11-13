# SmartAudioSwitcher Pro

Aplicativo experimental para gerenciar as saídas de áudio do Windows, com interface WPF, bandeja, perfis, CLI e API local.

> **Estado: desenvolvimento / pré-release.** Compilar não comprova compatibilidade com todos os drivers e versões do Windows. Não há instalador MSIX assinado ou pacote winget publicado. As releases antigas podem não conter binários.

## O que está implementado

- Enumeração de dispositivos e sessões de áudio reais com NAudio.
- Backend de troca do dispositivo padrão com verificação dos papéis Console, Multimedia e Communications.
- Registro de notificações de dispositivos; cache publicado antes dos eventos.
- Motor de perfis, cadeias de fallback e reconhecimento de nomes de processos.
- Canvas SonicFlow com persistência visual e validação de rotas.
- CLI com inicialização preguiçosa para ajuda/versão e erros com código de saída diferente de zero.
- API desativada por padrão, restrita a loopback quando habilitada.
- Testes automatizados sem troca do áudio do computador e workflow Windows de build/teste/publicação portátil.

## Limitações importantes

- **Roteamento por aplicativo ainda não é suportado pelo backend Windows.** O serviço retorna falha explicitamente; uma conexão desenhada no SonicFlow não significa que o áudio foi redirecionado.
- A troca nativa do dispositivo padrão precisa de validação manual em hardware real; a suíte automática não troca a saída do usuário.
- O registro/desregistro das notificações foi exercitado sem provocar hotplug físico. Bluetooth, HDMI, suspensão e retomada precisam de uma matriz manual de testes.
- Sessões são observadas por polling; sessões muito breves podem não aparecer.
- Detecção física de dock, medição visual de áudio, atualização automática, limite de volume de perfil e roteamento por chamada não devem ser considerados recursos concluídos.
- `allow_remote: true` é rejeitado: autenticação e autorização remotas ainda não foram implementadas.
- Não há medição publicada que sustente uma promessa de consumo de memória/CPU.

## Compilar e executar

Requisitos: Windows 10/11 e SDK .NET 8.

```powershell
git clone https://github.com/SouzaF19/SmartAudioSwitcherPro.git
cd SmartAudioSwitcherPro
dotnet restore SmartAudioSwitcher.sln
dotnet build SmartAudioSwitcher.sln -c Release
dotnet test SmartAudioSwitcher.sln -c Release
```

Para iniciar a interface — **isto pode aplicar regras de áudio configuradas**:

```powershell
dotnet run --project SmartAudioSwitcher.App/SmartAudioSwitcher.App.csproj
```

Para gerar uma pasta portátil que inclui o runtime:

```powershell
dotnet publish SmartAudioSwitcher.App/SmartAudioSwitcher.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/SmartAudioSwitcher-Portable
```

Execute `SmartAudioSwitcher.App.exe` na pasta publicada. Não é necessário publicar a solução inteira. O workflow `.github/workflows/build.yml` disponibiliza a pasta como artefato quando executado no GitHub; isso não cria uma release automaticamente.

## CLI

```powershell
dotnet run --project SmartAudioSwitcher.CLI -- --help
dotnet run --project SmartAudioSwitcher.CLI -- version
dotnet run --project SmartAudioSwitcher.CLI -- list-devices
dotnet run --project SmartAudioSwitcher.CLI -- get-default
# Os comandos abaixo alteram áudio:
dotnet run --project SmartAudioSwitcher.CLI -- set-device "Headset"
dotnet run --project SmartAudioSwitcher.CLI -- set-default "ID-DO-DISPOSITIVO"
dotnet run --project SmartAudioSwitcher.CLI -- cycle
```

Use o ID quando houver nomes ambíguos. Ajuda e versão não devem inicializar hardware nem criar a configuração do usuário.

## Configuração

Arquivo: `%APPDATA%\SmartAudioSwitcher\config.yaml`.

O serviço mantém suporte aos arquivos camelCase antigos. O formato documentado utiliza nomes snake_case e perfis em lista, não um mapa `profiles.gaming`.

```yaml
general:
  start_minimized: true
  check_updates: false
  log_level: Information
  language: pt-BR

profiles:
  - name: Default
    when: []
    routing:
      default: speakers
      fallback_chain: [headset, hdmi, system_default]

hotkeys:
  cycle_output: Ctrl+Alt+O
  toggle_mute: Ctrl+Alt+M
  force_headset: Ctrl+Alt+H
  open_dashboard: Ctrl+Shift+A

api:
  enabled: false
  port: 52841
  allow_remote: false
```

Perfis habilitados são avaliados em ordem; o primeiro compatível vence. Condições de contexto devem ser atendidas, enquanto alternativas de processos permitem selecionar um dentre vários aplicativos. Nomes de processos não diferenciam maiúsculas/minúsculas e aceitam a extensão `.exe` opcional. O backend per-app continua pendente mesmo que existam exceções no perfil.

A API é um executável separado:

```powershell
dotnet run --project SmartAudioSwitcher.Api
```

Somente inicie depois de habilitá-la na configuração. Endpoints sob `/api/v1`, incluindo `/health`, `/devices`, `/profiles/active` e `/routing`. Não exponha o processo por proxy ou túnel: ele não possui autenticação.

## Estrutura

- `SmartAudioSwitcher.Core/`: contratos, modelos, perfis e configuração.
- `SmartAudioSwitcher.Platform/`: integração Windows, COM, dispositivos e sessões.
- `SmartAudioSwitcher.UI/`: controles visuais SonicFlow.
- `SmartAudioSwitcher.App/`: aplicação principal WPF, bandeja e atalhos.
- `SmartAudioSwitcher.CLI/` e `SmartAudioSwitcher.Api/`: interfaces de automação.
- `tests/`: regressões de perfis, configuração, comandos, controles WPF e backend.

As dependências mantêm suas próprias licenças. Consulte também os avisos em `SmartAudioSwitcher.Platform/CoreAudio/THIRD-PARTY-NOTICES.md`. Este README não substitui uma licença de distribuição do projeto.

## Próximas validações

1. Troca real de saídas e restauração, com confirmação em todos os papéis de áudio.
2. Desconexão USB, Bluetooth, HDMI, suspensão e retomada.
3. Execução prolongada, descarte de recursos e consumo de memória/CPU.
4. Roteamento Windows por aplicativo, antes de anunciar suporte no SonicFlow.
5. Pacote assinado, distribuição e revisão das licenças.
