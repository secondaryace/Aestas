namespace Aestas.Commands
open Aestas.Core
open Aestas.Commands.AestasScript
open Aestas.Commands.Compiler.Runtime
open Aestas.Prim

open System
open System.Net.Http

module InfoCommand =
    let execute executer env ctx args =
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
        ctx, Unit
    let make() = {
        name = "info"
        description = "Print system infos"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = execute
    }