namespace Aestas.Commands
open Aestas.Prim
open Aestas.Core
open Aestas.AutoInit
type RegenerateCommand() =
    interface ICommand with
        member this.Name = "regenerate"
        member this.Help = "Let the bot recall their latest message and generate a new one"
        member _.AccessibleDomain = CommandAccessibleDomain.All
        member _.Privilege = CommandPrivilege.Normal
        member this.Execute env args =
            match
                env.domain.Messages
                |> IListExt.tryFindBack (fun x -> x.SenderId = env.domain.Self.uid)
            with
            | None -> env.log "No message to regenerate"
            | Some msg ->
                match env.bot.Recall env.domain msg.MessageId |> Async.RunSynchronously with
                | Ok () ->
                    env.bot.SelfTalk env.domain None |> Async.Ignore |> Async.Start
                | Error _ -> ()
            Unit
    interface IAutoInit<ICommand, unit> with
        static member Init _ = RegenerateCommand() :> ICommand
