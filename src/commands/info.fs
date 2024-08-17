namespace Aestas.Commands
open Aestas.Prim
open Aestas.Core
open Aestas.AutoInit
module InfoCommand =
    type InfoCommand() =
        interface ICommand with
            member this.Name = "info"
            member this.Help = "Print system infos"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member this.Execute env args =
                let system = System.Environment.OSVersion.VersionString
                let machine = System.Environment.MachineName
                let cpuCore = System.Environment.ProcessorCount
                let cpuName = 
                    if System.Environment.OSVersion.Platform = System.PlatformID.Unix then
                        (bash "cat /proc/cpuinfo | grep 'model name' | uniq | cut -d ':' -f 2").Trim()
                    else
                        (cmd " wmic cpu get name | find /V \"Name\"").Trim()
                let ipinfo =
                    use web = new System.Net.Http.HttpClient()
                    web.GetStringAsync("http://ip-api.com/line/").Result.Split('\n')
                env.log $"""System Info:
| System: {system}
| Machine: {machine}
| CPU: {cpuName}
| CPU Core: {cpuCore}
| IP: {ipinfo[1]} {ipinfo[4]} {ipinfo[5]}, {ipinfo[11]}"""
                Unit
        interface IAutoInit<ICommand, unit> with
            static member Init _ = InfoCommand() :> ICommand