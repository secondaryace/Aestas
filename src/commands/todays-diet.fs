namespace Aestas.Commands
open Aestas.Core
open Aestas.Commands.AestasScript
open Aestas.Commands.Compiler.Runtime
open Aestas.Prim

module TodaysDietCommand =
    let execute executer env ctx args =
        let prompt = """为大家进行每日的饮食推荐，以以下格式：
今天吃：[甜点] [主食] [饮料]
最后还可以加上你的一些小小的寄语。"""
        [AestasText prompt] |> Some |> env.bot.SelfTalk env.domain |> Async.Ignore |> Async.Start; ctx, Unit
    let make() = {
        name = "今天吃什么"
        description = "推荐今天吃什么"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = execute
    }