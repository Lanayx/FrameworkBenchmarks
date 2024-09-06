namespace App

open System
open System.Diagnostics
open System.Threading
open Oxpecker
open System.Runtime.InteropServices

[<RequireQualifiedAccess>]
module HtmlViews =
    open Oxpecker.ViewEngine

    let private head, tail =
        (fun (content: HtmlElement) ->
            html() {
                head() {
                    title() { "Fortunes" }
                }
                body() {
                    table() {
                        tr() {
                            th() { "id" }
                            th() { "message" }
                        }
                        content
                    }
                }
            } :> HtmlElement
        ) |> RenderHelpers.prerender

    let fortunes (fortunesData: ResizeArray<Fortune>) =
        let fragment = __()
        for fortune in CollectionsMarshal.AsSpan fortunesData do
            tr() {
                td() { raw <| string fortune.id }
                td() { fortune.message }
            }
            |> fragment.AddChild
        RenderHelpers.combine head tail fragment

[<RequireQualifiedAccess>]
module HttpHandlers =
    open System.Text
    open Microsoft.AspNetCore.Http
    open SpanJson

    let private extra =
        {
            id      = 0
            message = "Additional fortune added at request time."
        }

    let rec private renderFortunes (ctx: HttpContext) (data: ResizeArray<Fortune>) =
        data.Add extra
        data.Sort FortuneComparer
        data |> HtmlViews.fortunes |> ctx.WriteHtmlViewChunked

    let mutable dbTime = 0L
    let mutable renderTime = 0L

    let fortunes : EndpointHandler =
        fun ctx ->
            task {
                let sw = Stopwatch.StartNew()
                let! dbFortunes = loadFortunes ()
                let x = sw.ElapsedMilliseconds
                Interlocked.Add(&dbTime, sw.ElapsedMilliseconds)
                do! renderFortunes ctx dbFortunes
                Interlocked.Add(&renderTime, sw.ElapsedMilliseconds - x)
                sw.Stop()
            }

    let singleQuery : EndpointHandler =
        fun ctx ->
            task {
                let! result = loadSingleRow()
                return! ctx.WriteJsonChunked result
            }

    let private parseQueries (ctx: HttpContext) =
        match ctx.TryGetRouteValue<string>("count") with
        | Some q ->
            match Int32.TryParse q with
            | true, q when q > 1 -> if q < 500 then q else 500
            | _, _ -> 1
        | _ -> 1

    let multipleQueries : EndpointHandler =
        fun ctx ->
            let count = parseQueries ctx
            task {
                let! results = loadMultipleRows count
                return! ctx.WriteJsonChunked results
            }

    let multipleUpdates : EndpointHandler =
        fun ctx ->
            let count = parseQueries ctx
            task {
                let! results = doMultipleUpdates count
                return! ctx.WriteJsonChunked results
            }

    let utf8Const (s: string): EndpointHandler =
        let result = s |> Encoding.UTF8.GetBytes
        fun ctx ->
            ctx.SetContentType("text/plain")
            ctx.WriteBytes(result)


    let jsonSimple value : EndpointHandler =
        fun ctx ->
            ctx.SetContentType("application/json")
            JsonSerializer.Generic.Utf8.SerializeAsync<_>(value, stream = ctx.Response.Body).AsTask()

    let endpoints =
        [|
            route "/plaintext" <| utf8Const "Hello, World!"
            route "/json" <| jsonSimple { message = "Hello, World!" }
            route "/fortunes" fortunes
            route "/db" singleQuery
            route "/queries/{count?}" multipleQueries
            route "/updates/{count?}" multipleUpdates
        |]

module Main =
    open Microsoft.AspNetCore.Builder
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.Hosting
    open Microsoft.Extensions.Logging

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        builder.Services
            .AddRouting()
            .AddOxpecker() |> ignore
        builder.Logging
            .ClearProviders() |> ignore
        let app = builder.Build()
        app
            .UseRouting()
            .UseOxpecker(HttpHandlers.endpoints) |> ignore
        app.Lifetime.ApplicationStopping.Register(fun () ->
            Console.WriteLine($"Fortunes db: {HttpHandlers.dbTime}ms, render: {HttpHandlers.renderTime}ms"))
        app.Run()
        0