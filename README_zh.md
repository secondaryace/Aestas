## **Aestas**
模块化跨平台Chatbot制作框架，使用F#编写。

一切使用F#代码配置，无需编写JSON或YAML。
## **构建**
Linux：
```bash
./aestas.sh build
```
Windows：
```pwsh
.\aestas.ps1 build
```
## **模块**
| 类型 | 文件 | .NET 类型 |
| --- | --- | --- |
| 核心 | src/core.fs | ```Aestas.Core.AestasCore``` |
| 自动初始化 | src/auto-init.fs | ```Aestas.AutoInit``` |
| 协议 | src/adapters/ | ```Aestas.Core.IProtocolAdapter``` |
| Bot | src/bots/ | ```Aestas.Core.AestasBot``` |
| 命令 | src/commands/ | ```Aestas.Core.ICommand``` |
| 内容解析器 | src/plugins/ | ```Aestas.AutoInit.IAutoInit<string*MappingContentCtor*SystemInstructionBuilder, unit>``` |
| 语言模型 | src/llms/ | ```Aestas.Core.ILanguageModelClient``` |
## **内置支持**
- [x] 控制台
- [x] QQ（通过Lagrange）
- [ ] Satori 
- [ ] WebUI (5%)