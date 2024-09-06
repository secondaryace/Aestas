## Aestas
A modular chatbot making framework, also cross platform, written in F#.

Configure everything in F# code, no need to write JSON or YAML.

**English** | [简体中文](README_zh.md)
## Build
Linux:
```bash
./aestas.sh build
```
Windows:
```pwsh
.\aestas.ps1 build
```
## Modules
| Type | File | .NET Type |
| --- | --- | --- |
| Core | src/core.fs | ```Aestas.Core.AestasCore``` |
| Auto Initializer | src/auto-init.fs | ```Aestas.AutoInit``` |
| Protocol | src/adapters/ | ```Aestas.Core.IProtocolAdapter``` |
| Bot | src/bots/ | ```Aestas.Core.AestasBot``` |
| Command | src/commands/ | ```Aestas.Core.ICommand``` |
| Content Parser | src/plugins/ | ```Aestas.AutoInit.IAutoInit<string*MappingContentCtor*SystemInstructionBuilder, unit>``` |
| LLM | src/llms/ | ```Aestas.Core.ILanguageModelClient``` |
## Built-in Support
- [x] Console
- [x] QQ (via Lagrange)
- [ ] Satori
- [ ] WebUI (5%)