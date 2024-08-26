namespace Aestas.Commands
open System
open System.Net.Http
open Aestas.Prim
open Aestas.Core
open Aestas.AutoInit
type InfoCommand() =
    interface ICommand with
        member this.Name = "info"
        member this.Help = "Print system infos"
        member _.AccessibleDomain = CommandAccessibleDomain.All
        member _.Privilege = CommandPrivilege.Normal
        member this.Execute env args =
            let system = Environment.OSVersion.VersionString
            let machine = Environment.MachineName
            let cpuCore = Environment.ProcessorCount
            let cpuName = 
                if Environment.OSVersion.Platform = PlatformID.Unix then
                    (bash "cat /proc/cpuinfo | grep 'model name' | uniq | cut -d ':' -f 2").Trim()
                else
                    (cmd "wmic cpu get name | find /V \"Name\"").Trim()
            let ipinfo =
                use web = new HttpClient()
                web.GetStringAsync("http://ip-api.com/line/").Result.Split('\n')
            let heap = (GC.GetTotalMemory true |> float) / 1024.0 / 1024.0
            let pwd = Environment.CurrentDirectory
            let pid = Environment.ProcessId
            env.log $"""System Info:
| System: {system}
| Machine: {machine}
| CPU: {cpuName}, {cpuCore} cores
| IP: {ipinfo[1]} {ipinfo[4]} {ipinfo[5]}, {ipinfo[11]}
| Managed Heap: {heap:N4} MB
| Process ID: {pid}
| Working Directory: {pwd}"""
            Unit
    interface IAutoInit<ICommand, unit> with
        static member Init _ = InfoCommand() :> ICommand