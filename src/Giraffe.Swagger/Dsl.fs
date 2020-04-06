namespace Giraffe.Swagger

open System
open Microsoft.FSharp.Quotations
open Microsoft.AspNetCore.Http
open Microsoft.FSharp.Quotations.Patterns
open FSharp.Control.Tasks.V2.ContextInsensitive
open Giraffe
open Giraffe.Swagger.Analyzer
open Giraffe.Swagger.Generator
open Giraffe.Swagger.UI

module Dsl =
    // used to facilitate quotation expression analysis
    let (==>) = (>=>)

    let operationId (opId : string) next = next
    let consumes (modelType : Type) next = next
    let produces (modelType : Type) next = next

    let definitionOfType (t : Type) = t.Describes()

    let swaggerDoc docCtx addendums (description : ApiDescription -> ApiDescription) schemes host basePath =
        let rawJson (str : string) : HttpHandler =
            setHttpHeader "Content-Type" "application/json"
                >=> fun (next : HttpFunc) (ctx : HttpContext) -> ctx.WriteStringAsync str

        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let paths = documentRoutes docCtx.Routes addendums
                let definitions =
                    paths
                    |> Seq.collect(
                        fun p ->
                            p.Value
                            |> Seq.collect (
                                fun v ->
                                    let inputTypes =
                                         v.Value.Parameters
                                        |> List.choose(fun (r : ParamDefinition) -> r.Type)
                                        |> List.choose(fun (r : PropertyDefinition) ->
                                            match r with
                                            | Ref objectDef -> Some objectDef
                                            | _ -> None)
                                        |> List.collect(fun d -> d.FlattenComplexDefinitions())
                                    let outputTypes =
                                        v.Value.Responses
                                        |> Seq.choose(fun r -> r.Value.Schema)
                                        |> Seq.collect(fun d -> d.FlattenComplexDefinitions())
                                        |> Seq.toList
                                    (inputTypes @ outputTypes))
                    )
                    |> Seq.toList
                    |> List.distinct
                let doc =
                    {
                        Swagger     = "2.0"
                        Info        = description ApiDescription.Empty
                        BasePath    = basePath
                        Host        = host
                        Schemes     = schemes
                        Paths       = paths
                        Definitions = (definitions |> List.map (fun d -> d.Id, d) |> Map)
                    }
                return! rawJson (doc.ToJson()) next ctx
            }

    type DocumentationConfig =
        {
            MethodCallRules         : Map<MethodCallId, AnalyzeRuleBody> -> Map<MethodCallId, AnalyzeRuleBody>
            DocumentationAddendums  : DocumentationAddendumProvider
            Description             : ApiDescription -> ApiDescription
            BasePath                : string
            Host                    : string
            Schemes                 : string list
            SwaggerUrl              : string
            SwaggerUiUrl            : string
        }
        static member Default =
            {
                MethodCallRules = fun m -> m
                Description     = fun d -> d
                BasePath        = "/"
                Host            = "localhost"
                Schemes         = [ "http" ]
                DocumentationAddendums = DefaultDocumentationAddendumProvider
                SwaggerUrl      = "/swagger.json"
                SwaggerUiUrl    = "/swaggerui/"
            }

    type swaggerOf
        (
            [<ReflectedDefinition(true)>] webappWithVal : Expr<HttpFunc -> HttpContext -> HttpFuncResult>
        ) =
        member __.Documents (configuration : DocumentationConfig -> DocumentationConfig) =
            match webappWithVal with
            | WithValue(v, ty, webapp) ->
                let app     = unbox<HttpHandler> v
                let config  = configuration DocumentationConfig.Default
                let rules   = {
                    AppAnalyzeRules.Default
                        with MethodCalls = (config.MethodCallRules AppAnalyzeRules.Default.MethodCalls) }
                let docCtx  = analyze webapp rules
                let webPart =
                    swaggerDoc
                        docCtx
                        config.DocumentationAddendums
                        config.Description
                        config.Schemes
                        config.Host
                        config.BasePath
                let swaggerJson = route config.SwaggerUrl >=> webPart
                choose [
                    swaggerJson
                    swaggerUiHandler config.SwaggerUiUrl config.SwaggerUrl
                    app ]
            | other ->
                failwith "Invalid arg"

    let withConfig configuration (s : swaggerOf) = s.Documents configuration