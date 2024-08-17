## Aestas
A modular chatbot making framework, also cross platform, written in F#.

Configure everything in F# code, no need to write JSON or YAML.

**English** | [简体中文](README_zh.md)
## Build
```bash
dotnet fsi fsproj.fsx
dotnet build
```
## Modules
| Type | File | .NET Type |
| --- | --- | --- |
| Core | src/core.fs | ```Aestas.Core.AestasCore``` |
| Auto Initializer | src/auto-init.fs | ```Aestas.AutoInit``` |
| Protocol | src/adapters/ | ```Aestas.Core.IProtocolAdapter``` |
| Bot | src/bots/ | ```Aestas.Core.AestasBot``` |
| Command | src/commands/ | ```Aestas.Core.ICommand``` |
| Plugin | src/plugins/ | ```Aestas.Core.IAestasMappingContent``` |
| Plugin | src/plugins/ | ```Aestas.Core.IProtocolSpecifyContent```|
| LLM | src/llms/ | ```Aestas.Core.ILanguageModelClient``` |
## Built-in Support
- [x] Console
- [x] QQ (via Language)
- [ ] Satori