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
            env.log $"Aestas version {version}"
            ctx, Unit
        }
    let clear() = {
        name = "clear"
        description = "Clear the cached context"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = fun executer env ctx args -> 
            env.bot.ClearCachedContext env.domain
            env.log "Cached context cleared"
            ctx, Unit
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
            ctx, Unit
    }