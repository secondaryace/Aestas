namespace Aestas.Test.CSharp;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Collections;
using static Aestas.Core;
using static Aestas.AutoInit;

public class CSharpInfoCommand : ICommand, IAutoInit<ICommand, Unit>
{
    public string Name => "Info";
    public string Help => "Print system infos";
    public CommandAccessibleDomain AccessibleDomain => CommandAccessibleDomain.All;
    public CommandPrivilege Privilege => CommandPrivilege.Normal;
    public Atom Execute(CommandEnvironment env, FSharpList<Atom> args)
    {
        var system = Environment.OSVersion.VersionString;
        var machine = Environment.MachineName;
        var cpuCore = Environment.ProcessorCount;
        env.log.Invoke($"""
        System Info:
         | System: {system}
         | Machine: {machine}
         | CPU: {cpuCore} cores
        """);
        return Atom.Unit;
    }
    public static ICommand Init(Unit _)
    {
        return new CSharpInfoCommand();
    }
}
