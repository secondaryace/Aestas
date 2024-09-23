open System
open System.IO
open System.Collections.Generic

let settings = fsi.CommandLineArgs[1..]
let profile = if settings |> Array.contains "--debug" then "Debug" else "Release"
let verbose = settings |> Array.contains "--verbose"
let getFiles path = 
    if Directory.Exists path then
        let files = Directory.GetFiles path 
        let source = files |> Array.filter (fun s -> s.EndsWith ".fs")
        let ignore' = 
            files 
            |> Array.filter (fun s -> s.EndsWith ".fs.ignore") 
            |> Array.map (fun s -> s.Substring(0, s.Length - 7), ())
            |> Map.ofArray
        source |> Array.filter (fun s -> ignore' |> Map.containsKey s |> not) |> Array.sortDescending
    else [||]
let ( @@ ) (a: 't[]) (b: 't[]) = Array.concat [a; b]
let checkDir dir = if Directory.Exists dir then () else Directory.CreateDirectory dir |> ignore
checkDir "src"
checkDir "src/misc"
checkDir "src/adapters"
checkDir "src/bots"
checkDir "src/commands"
checkDir "src/llms"
checkDir "src/plugins"
let misc = getFiles "src/misc/"
let adapters = getFiles "src/adapters/"
let bots = getFiles "src/bots/"
let commands = getFiles "src/commands/"
let llms = getFiles "src/llms/"
let plugins = getFiles "src/plugins/"
let all = misc @@ adapters @@ bots @@ commands @@ llms @@ plugins
let proj = ResizeArray<string>()
let nuget = ResizeArray<string*string>()
let foldIList (folder: 'state -> 't -> 'state) (state: 'state) (list: IList<'t>) =
    let rec go i state =
        if i >= list.Count then state
        else
            go (i+1) (folder state list[i])
    go 0 state
let src x = $"src/{x}"
let parseRef (r: string) =
    let span = r.AsSpan().Slice 4
    if span.StartsWith "csproj" || span.StartsWith "fsproj" then
        let path = span.Slice 7
        proj.Add(path.ToString())
    elif span.StartsWith "nuget" then
        let path = span.Slice 6
        let spiltIndex = path.IndexOf '='
        nuget.Add(path.Slice(0, spiltIndex).ToString(), path.Slice(spiltIndex+1).ToString())
    else ()
let findRef (reader: StreamReader) = 
    let rec go() =
        let line = reader.ReadLine()
        if line |> String.IsNullOrEmpty |> not && line.StartsWith "//!" then
            if verbose then printfn "Finded ref: %s" line
            parseRef line
            go()
        else ()
    go()
all |> Array.iter (
    fun x -> 
        if verbose then printfn "Finded file: %s" x
        use reader = new StreamReader(x)
        findRef reader
        )
if verbose then printfn "Project references: %A" proj
if verbose then printfn "Package references: %A" nuget
let spaceLine = ""
let projectStart = """<Project Sdk="Microsoft.NET.Sdk">"""
let projectEnd = """</Project>"""
let propertyGroup outputType outputPath cargs = $"""  <PropertyGroup>
    <OutputType>{outputType}</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <InvariantGlobalization>true</InvariantGlobalization>
    <LangVersion>preview</LangVersion>
    <PlatformTarget>AnyCPU</PlatformTarget>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <NoWarn>3535, 3536</NoWarn>
    <BaseOutputPath>{outputPath}</BaseOutputPath>
    <OtherFlags>{cargs}</OtherFlags>
  </PropertyGroup>"""
let itemGroupStart = """  <ItemGroup>"""
let itemGroupEnd = """  </ItemGroup>"""
let compileInclude x = $"""    <Compile Include="{x}" />"""
let packageReference n v = $"""    <PackageReference Include="{n}" Version="{v}" />"""
let packageReferenceUpdate n v = $"""    <PackageReference Update="{n}" Version="{v}" />"""
let projectReference p = $"""    <ProjectReference Include="{p}" />"""
let dllReference n p = $"""    <Reference Include="{n}">
      <HintPath>{p}</HintPath>
    </Reference>"""
let coreXml = [
    [projectStart]
    [spaceLine]
    [propertyGroup "Library" "bin/core/" "--allsigs --test:GraphBasedChecking --test:ParallelOptimization --test:ParallelIlxGen"]
    [spaceLine]
    [itemGroupStart]
    [
        "prim.fs" |> src |> compileInclude
        "core.fs" |> src |> compileInclude
        "auto-init.fs" |> src |> compileInclude
    ]
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    [packageReference "FSharp.SystemTextJson" "1.3.13"]
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    [packageReferenceUpdate "FSharp.Core" "8.0.400"]
    [itemGroupEnd]
    [spaceLine]
    [projectEnd]
]
let launchXml = [
    [projectStart]
    [spaceLine]
    [propertyGroup "Exe" "bin" "--test:GraphBasedChecking --test:ParallelOptimization --test:ParallelIlxGen"]
    [spaceLine]
    [itemGroupStart]
    misc |> foldIList (fun list x -> compileInclude x::list) []
    llms |> foldIList (fun list x -> compileInclude x::list) []
    adapters |> foldIList (fun list x -> compileInclude x::list) []
    plugins |> foldIList (fun list x -> compileInclude x::list) []
    commands |> foldIList (fun list x -> compileInclude x::list) []
    bots |> foldIList (fun list x -> compileInclude x::list) []
    if settings |> Array.contains "--nocli" then [] else ["cli.fs" |> src |> compileInclude]
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    [packageReference "FSharp.SystemTextJson" "1.3.13"]
    [packageReference "FSharpPlus" "1.6.1"]
    nuget |> foldIList (fun list (n, v) -> packageReference n v::list) []
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    proj |> foldIList (fun list x -> projectReference x::list) []
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    [dllReference "Aestas.Core" $"bin/core/{profile}/net8.0/Aestas.Core.dll"]
    [itemGroupEnd]
    [itemGroupStart]
    [packageReferenceUpdate "FSharp.Core" "8.0.400"]
    [itemGroupEnd]
    [spaceLine]
    [projectEnd]
]
let xmlString xml = xml |> List.collect id |> String.concat "\n"
let write (path: String) (str: string) =
    use writer = new StreamWriter(path)
    writer.Write str
    writer.Flush()
    printfn "Generated %s" path
if settings |> Array.contains "--nocore" |> not then coreXml |> xmlString |> write "Aestas.Core.fsproj"
launchXml |> xmlString |> write "aestas.fsproj"
