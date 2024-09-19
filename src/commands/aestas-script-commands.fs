namespace Aestas.Commands
open System.Text
open Aestas.Commands.Compiler.Runtime
open Aestas.Prim
open Aestas.Core
open Aestas.Commands.AestasScript

module AestasScriptCommands =
    let version() = {
        name = "version"
        description = "Print the version of Aestas"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env ctx args -> 
            env.log $"Aestas version {version}\nAestas Script"
            ctx, Tuple []
        }
    let clear() = {
        name = "clear"
        description = "Clear the cached context"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env ctx args -> 
            env.bot.ClearCachedContext env.domain
            env.log "Cached context cleared"
            ctx, Tuple []
        }
    let help() = {
        name = "help"
        description = "List all commands"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env ctx args -> 
            let sb = StringBuilder()
            sb.Append "## Commands" |> ignore
            executer.Commands |>
            ArrList.iter (fun v -> sb.Append $"\n* {v.name}:\n   {v.description}" |> ignore)
            sb.ToString() |> env.log
            ctx, Tuple []
        }
    let dump() = {
        name = "dump"
        description = "Dump the cached context"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env ctx args -> 
            ctx, 
            env.domain.Messages |> List.ofSeq |> List.map (fun m -> 
                let msg = m.Parse()
                Map.ofList [
                    "sender", msg.sender.name |> string |> String
                    "mid", msg.mid |> float |> Number
                    "contents", msg.contents |> string |> String
                ] |> Object
            ) |> Tuple
        }