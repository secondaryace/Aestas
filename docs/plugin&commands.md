## 起步-使用自动初始化资源为bot添加功能
本文假定你已了解如何[创建bot](bot.md)并[自动初始化](autoinit.md)。接下来，我们将使用自动初始化资源为bot添加功能。
在[创建bot](bot.md)中所写代码的基础上，以内置的非`Aestas.Core`模块的功能为例，本文将创建一个基本的QQ bot。
### 绑定到QQ群聊所对应的域
```fsharp
open Aestas.Adapters.AestasLagrangeBot
```
确保已打开`Aestas.Adapters.AestasLagrangeBot`模块。
```fsharp
type MyFirstBotInitializer =
    interface IAutoInit<AestasBot, unit> with
        static member Init _ =
            let bot = AestasBot()
            ...
            getProtocol<LagrangeAdapter>().InitDomainView(bot, (*QQ群号/QQ号*)) |> bot.BindDomain 
            ...
```
`LagrangeAdapter`会在第一次使用时阻塞自动初始化并在控制台打印出QQ登录二维码，扫码登录即可，之后应会自动登录。`InitDomainView`的第二个参数是QQ群号/QQ号，这里填写你的QQ群号/QQ号，如果你登录的QQ存在ID相等的群和好友，会出现未定义行为，概率相当小，但并非不存在。
### 添加表情包功能
使用`AutoInit`模块中的函数来获取资源，然后将其添加到bot中。这里的`BotHelper`是一个工具模块，包含各种方便的函数。
```fsharp
            getMappingContentCtorTip<StickerContent>() |> BotHelper.addMappingContentCtorTip bot
```
`StickerContent`是`AestasMappingContent`中可以包装的类之一，代表一个待映射的`AestasContent`，简单来讲就是从文字转换到表情包，上述的代码会将它如何构造以及模型所需要的提示添加到bot中。

当然，表情包可不止需要这么一点东西，显然我们还没有指定需要什么表情包。要提供额外的信息，需要修改`bot.ExtraData`。

`bot.ExtraData`内部实际上是一个字典，我们用`AddExtraData`方法向其中添加键值对。
```fsharp
            bot.AddExtraData "stickers" {
                stickers = dict' [
                    "欸嘿", {path = "media/stickers/chuckle.png"; width = 100; height = 100}
                    "呜呜呜", {path = "media/stickers/cry.png"; width = 100; height = 100}
                ]
            }
```
### 添加命令