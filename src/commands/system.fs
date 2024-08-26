namespace Aestas.Commands
open System
open Aestas.Prim
open Aestas.Core
open Aestas.AutoInit
type SystemCommand() =
    interface ICommand with
        member this.Name = "system"
        member this.Help = "Use system shell commands"
        member _.AccessibleDomain = CommandAccessibleDomain.All
        member _.Privilege = CommandPrivilege.High
        member this.Execute env args =
            match args with
            | Identifier "pwsh"::String c::[] -> 
                let output = pwsh c
                env.log output
            | String c::[] ->
                let output =
                    if Environment.OSVersion.Platform = PlatformID.Unix then
                        bash c
                    else
                        cmd c
                env.log output
            | _ -> env.log "Invalid arguments"
            Unit
    interface IAutoInit<ICommand, unit> with
        static member Init _ = SystemCommand() :> ICommand