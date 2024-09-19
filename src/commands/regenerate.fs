namespace Aestas.Commands
open Aestas.Core
open Aestas.Commands.AestasScript
open Aestas.Commands.Compiler.Runtime
open Aestas.Prim

module RegenerateCommand =
    let execute executer env ctx args =
        match
            env.domain.Messages
            |> IList.tryFindBack (fun x -> x.SenderId = env.domain.Self.uid)
        with
        | None -> env.log "No message to regenerate"
        | Some msg ->
            match env.bot.Recall env.domain msg.MessageId |> Async.RunSynchronously with
            | Ok () ->
                env.bot.SelfTalk env.domain None |> Async.Ignore |> Async.Start
            | Error _ -> ()
        ctx, Tuple []
    let make() = {
        name = "regenerate"
        description = "Let the bot recall their latest message and generate a new one"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = execute
    }