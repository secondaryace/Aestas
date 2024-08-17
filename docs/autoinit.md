## 起步-自动初始化
首先介绍`AutoInit`模块。

`AutoInit`是Aestas默认用于自动初始化Bot，插件，指令的模块。如果你通过Cli使用Aestas，推荐你通过`AutoInit`来自动初始化你为Aestas新增的代码，而不需要修改Aestas的源代码，免于Git冲突。

`AutoInit`模块中的`IAutoInit<'t, 'tArg>`是自动初始化的类需要实现的接口。`'t`是你要初始化的类型，`'tArg`是初始化时需要的参数类型。一般来说，`'tArg`是`unit`，也就是不需要参数，这里保留了这个参数是为了以后的扩展。`IAutoInit<'t, 'tArg>`要求实现一个静态抽象方法`Init`，这并非F#推荐的用法，所以会提示警告。但为了保证在编译时检查类型的正确性，AutoInit仍然采取这种写法。

事实上，自动初始化还需一个脚本文件`prepare.fsx`配合，该脚本在编译前扫描所有特定文件夹中的文件（不需要其中的类实现`IAutoInit`），并生成`aestas.fsproj`，用于后续`dotnet`命令行编译整个项目。对于这些被扫描的文件，可以用一些特殊的注释来让脚本给项目加入额外引用的包和项目。下面是格式：
```fsharp
//! csproj {path}
//! fsproj {path}
//! nuget {name}={versionNumber}
```

脚本不会处理引用冲突，所以请确保所有文件所想要引用的包和项目都不会产生冲突。