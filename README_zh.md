## **Aestas**
模块化跨平台Chatbot制作框架，使用F#编写。

一切使用F#代码配置，无需编写JSON或YAML。
## **构建**
```bash
dotnet fsi fsproj.fsx
dotnet build
```
## **模块**
| 类型 | 文件 | .NET 类型 |
| --- | --- | --- |
| Core | src/core.fs | ```Aestas.Core.AestasCore``` |
| Auto Initializer | src/auto-init.fs | ```Aestas.AutoInit``` |
| Protocol | src/adapters/ | ```Aestas.Core.IProtocolAdapter``` |
| Bot | src/bots/ | ```Aestas.Core.AestasBot``` |
| Command | src/commands/ | ```Aestas.Core.ICommand``` |
| Plugin | src/plugins/ | ```Aestas.Core.IAestasMappingContent``` |
| Plugin | src/plugins/ | ```Aestas.Core.IProtocolSpecifyContent```|
| LLM | src/llms/ | ```Aestas.Core.ILanguageModelClient``` |
## **内置支持**
- [x] 控制台
- [x] QQ（通过Language）
- [ ] Satori 