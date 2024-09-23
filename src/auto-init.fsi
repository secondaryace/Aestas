namespace Aestas
    
    /// AutoInit is a type-safe auto initializer.
    /// It will scan all types in the current app domain and initialize all types that implement IAutoInit.
    module AutoInit =
        
        type IAutoInit<'t,'tArg> =
            
            static abstract Init: 'tArg -> 't
        
        val private _protocols:
          System.Collections.Generic.Dictionary<System.Type,
                                                Core.IProtocolAdapter>
        
        val private _bots: Core.AestasBot Prim.arrList
        
        val private _mappingContentCtorTips:
          System.Collections.Generic.Dictionary<System.Type,
                                                (Core.ContentParser * string *
                                                 (Core.AestasBot ->
                                                    System.Text.StringBuilder ->
                                                    unit))>
        
        val private _protocolContentCtorTips:
          System.Collections.Generic.Dictionary<System.Type,
                                                (Core.ProtocolSpecifyContentCtor *
                                                 string *
                                                 (Core.AestasBot -> string))>
        
        val private _commands:
          System.Collections.Generic.Dictionary<System.Type,Core.CommandExecuter>
        
        val bots: System.Collections.Generic.IReadOnlyList<Core.AestasBot>
        
        val protocols:
          System.Collections.Generic.IReadOnlyDictionary<System.Type,
                                                         Core.IProtocolAdapter>
        
        val mappingContentCtorTips:
          System.Collections.Generic.IReadOnlyDictionary<System.Type,
                                                         (Core.ContentParser *
                                                          string *
                                                          (Core.AestasBot ->
                                                             System.Text.StringBuilder ->
                                                             unit))>
        
        val protocolContentCtorTips:
          System.Collections.Generic.IReadOnlyDictionary<System.Type,
                                                         (Core.ProtocolSpecifyContentCtor *
                                                          string *
                                                          (Core.AestasBot ->
                                                             string))>
        
        val commands:
          System.Collections.Generic.IReadOnlyDictionary<System.Type,
                                                         Core.CommandExecuter>
        
        val inline invokeInit: t: System.Type -> arg: 'tArg -> 't
        
        val inline tryGetCommandExecuter:
          s: string -> Core.CommandExecuter option
        
        val inline tryGetProtocol: s: string -> Core.IProtocolAdapter option
        
        val inline tryGetContentParserTip:
          s: string ->
            (Core.ContentParser * string *
             (Core.AestasBot -> System.Text.StringBuilder -> unit)) option
        
        val inline tryGetProtocolContentCtorTip:
          s: string ->
            (Core.ProtocolSpecifyContentCtor * string *
             (Core.AestasBot -> string)) option
        
        val inline getCommandExecuter<'t when 't :> Core.CommandExecuter> :
          unit -> Core.CommandExecuter when 't :> Core.CommandExecuter
        
        val inline getProtocol<'t when 't :> Core.IProtocolAdapter> :
          unit -> Core.IProtocolAdapter when 't :> Core.IProtocolAdapter
        
        val inline getContentParser<'t
                                      when 't :>
                                                IAutoInit<(string *
                                                           Core.ContentParser *
                                                           (Core.AestasBot ->
                                                              System.Text.StringBuilder ->
                                                              unit)),unit>> :
          unit ->
            Core.ContentParser * string *
            (Core.AestasBot -> System.Text.StringBuilder -> unit)
            when 't :>
                      IAutoInit<(string * Core.ContentParser *
                                 (Core.AestasBot ->
                                    System.Text.StringBuilder -> unit)),unit>
        
        val inline getProtocolContentCtorTip<'t
                                               when 't :>
                                                         Core.IProtocolSpecifyContent> :
          unit ->
            Core.ProtocolSpecifyContentCtor * string *
            (Core.AestasBot -> string) when 't :> Core.IProtocolSpecifyContent
        
        [<Struct>]
        type InitTypes =
            | Ignore = 101
            | Bot = 100
            | Protocol = 0
            | ProtocolPlugin = 1
            | ContentParser = 2
            | Command = 3
        
        val mutable _initializers: (InitTypes * (unit -> unit)) array option
        
        val _init: initTypes: Set<InitTypes> -> unit
        
        val init: force: bool -> initTypes: Set<InitTypes> -> unit
        
        val initAll: unit -> unit

