## 起步-创建Bot
### 在起步之前
在尝试用Aestas制作自己的机器人之前，你可能需要了解F#的基础语法。当然，Aestas支持使用C#编写插件，通过加载dll的方式来使用，所以你至少要入门其中一种语言。在了解F#时，多多参考MSDN中的[F#文档](https://learn.microsoft.com/zh-cn/dotnet/fsharp/what-is-fsharp)和[.NET API文档](https://learn.microsoft.com/zh-cn/dotnet/api/?view=net-8.0)。如果你已经是F#高手，可以跳过这一步。如果你是C#高手，可以参照C#的示例代码和文档。如果你对这两种语言都不熟悉，而且很想要快速创建一个Bot，可以参照本文，也可以直接复制并修改示例代码。
### 打开Aestas模块
```fsharp
open Aestas.Prim
open Aestas.Core
open Aestas.Core.AestasBot
open Aestas.AutoInit
```
### 编写Bot创建代码
#### 创建第一个Bot
Aestas使用`AutoInit`模块来自动初始化，你需要编写一个实现`IAutoInit`接口的类来自动初始化Bot。除此之外也有其它方法，但是`AutoInit`是灵活且易于维护和使用的。接口的使用见[MSDN](https://learn.microsoft.com/zh-cn/dotnet/fsharp/language-reference/interfaces)。
这里是一个最简单的Bot创建代码：
```fsharp
module MyFirstBot = //可选的，模块并不会影响Bot的加载
    type MyFirstBotInitializer() =
        interface IAutoInit<MyFirstBot, unit> with
            member this.Init() =
                let bot = createBotShort "MyFirstBot" // 创建一个名为MyFirstBot的Bot
                // 在下面的教程中给出的代码实际上写在这里，而不是下两行的位置
                bot // 在F#中，最后一个表达式的值就相当于返回值
```
这样你就创建了一个不具有任何功能的Bot，接下来我们为它添加功能。
*提示：在继续写代码之前，更新fsproj文件，使现在的文件可以被代码高亮*
```bash
./aestas prepare
```
Aestas比较特殊的一点是，它不需要我们编写一个配置文件。你可以自己进行序列化和反序列化，但是Aestas并不要求你这么做。这样的好处是，你可以用更灵活的而且有更强自动补全的代码来替换JSON和YAML之类的配置文件，大多错误都会在编译时被发现，而不是运行时。
Aestas使用F#，并不具有动态语言的热重载功能，每当你增加一个功能都必须要重新编译，而且F#的编译速度很慢。但假如我们能通过IDE和编译器、类型系统来找出大多数错误，也就不需要频繁地去编译和手动重启。当然事实上用TypeScript和Python还是会更快乐一些，Aestas用F#主打一个函数式编程的新奇体验，运行速度或许也比脚本语言做的框架快一些。
```fsharp
                let model = GeminiLlm({
                apiKey = Some "{这里填写你的API key}" // Some代表F#中的option，代表这个值是确定有值的可空值，参考python的Optional，C#的Nullable
                gcloudPath = tryGetEnv "GCLOUD_PATH" // 这里填写gcloud路径，这里使用环境变量，不存在这个变量会设置为None
                safetySettings = [|
                    {category = "HARM_CATEGORY_HARASSMENT"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_HATE_SPEECH"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_SEXUALLY_EXPLICIT"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_DANGEROUS_CONTENT"; threshold = "BLOCK_SOME"}
                |] // 如果没有特殊需求，只需要照抄
                generation_configs = {
                    defaultGenerationConfig with // 这是F#的record表达式，在提供的默认配置上修改
                        temperature = Some 1.
                        maxLength = Some 4096
                        topK = Some 64
                        topP = Some 1.
                } |> Some
                }, 
                "gemini-1.5-flash") // 最后的字符串代表使用Gemini1.5的Flash模型，"gemini-1.5-pro"则使用Pro模型
                model |> bindModel bot // 将模型绑定到Bot上
                // |> 是F#中的管道运算符，x |> f 等价于 f x
```
这样就让你的Bot拥有了一个可以用来对话的模型，但我们目前还没有和Bot对话的手段，所以我们将Bot绑定到域（`AestasDomain`）上。
为了测试，我们使用控制台域，这样我们就可以在控制台上和Bot对话。
```fsharp
                ConsoleBot.singleton.InitDomainView(bot, 0u) |> bindDomain bot // 0u是控制台域的域ID，它可能且仅可能是0
                // u代表了uint32，是F#中的无符号32位整数
```
这样下来，我们就可以在控制台上和Bot对话。在正常情况下，我们会将Bot绑定到一个真实的聊天软件的群聊/私聊上，我们统称其为一种域视图，简称域。事实上的同一个群聊可以对应不同的域，因为在每个Bot眼里它们的表现都可以是不同的。因此域是一个仅对于单个Bot有意义的概念。大多数情况下对域的绑定也是一次性的，Bot不会将域解绑。在Aestas的基础上当然可以实现动态加/退群聊，但我并没有维护这个的动力。
#### 修改Bot的属性
```
在大多时候，我们会想要给模型一个系统提示，我们可以为Bot设置一个系统提示：
```fsharp
                "你好，我是MyFirstBot，我可以回答你的问题。" |> addSystemInstruction bot // 这里填写你的系统提示
```
在所绑定的模型获取系统提示时，也说通过`bot.SystemInstruction`，注意这并非一个字段，而是属性。
`bot.SystemInstructionBuilder`是一个`PipeLineChain<AestasBot * StringBuilder> option`类型的属性，默认为`None`，你可以这样设置`bot.SystemInstructionBuilder`：
```fsharp
                (fun (bot, stringBuilder) -> $"[Time is:{System.DateTime.Now}]" |> stringBuilder.Append |> ignore)
                |> addSystemInstructionBuilder bot
```
这样，获取到的系统提示的结果就会变为类似
```
你好，我是MyFirstBot，我可以回答你的问题。
[Time is:2024/8/1 0:12:34]
```
不影响原来设置的系统提示的内容。

`StringBuilder`是.NET中的类，如果熟悉C#，你可能会知道它的用法。`AestasBot * StringBuilder`实际上是一个ADT（代数数据类型）样式的元组类型，在C#看来是这样：`Tuple<AestasBot, StringBuilder>`。`PipeLineChain<>`实际上只是将一些函数组合在一起链式调用的类，定义很简单。

#### 小结
本文介绍了如何从源代码创建一个最基本的bot。至于更详细的bot的各种功能，我们将在Notebook的交互式环境中演示。