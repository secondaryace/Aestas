namespace Aestas.Commands
open Aestas.Core
open Aestas.AutoInit
type TodaysDietCommand() =
    interface ICommand with
        member this.Name = "今天吃什么"
        member this.Help = "推荐今天吃什么"
        member _.AccessibleDomain = CommandAccessibleDomain.All
        member _.Privilege = CommandPrivilege.Normal
        member this.Execute env args =
            let prompt = """为大家进行每日的饮食推荐，以以下格式：
今天吃：[甜点] [主食] [饮料]
最后还可以加上你的一些小小的寄语。"""
            [AestasText prompt] |> Some |> env.bot.SelfTalk env.domain |> Async.Ignore |> Async.Start; Unit
    interface IAutoInit<ICommand, unit> with
        static member Init _ = TodaysDietCommand() :> ICommand