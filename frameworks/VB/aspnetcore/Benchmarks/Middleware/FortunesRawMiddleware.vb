' Copyright (c) .NET Foundation. All rights reserved.
' Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

Imports System.Runtime.CompilerServices
Imports System.Text.Encodings.Web
Imports System.Threading
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection

Public Class FortunesRawMiddleware

    Private ReadOnly NextStage As RequestDelegate
    Private ReadOnly Encoder As HtmlEncoder
    Public Shared DbTime As Int64
    Public Shared RenderTime as Int64

    Public Sub New(ByVal NextStage As RequestDelegate, ByVal htmlEncoder As HtmlEncoder)
        Me.NextStage = NextStage
        Encoder = htmlEncoder
    End Sub

    Public Async Function Invoke(ByVal httpContext As HttpContext) As Task

        If httpContext.Request.Path.StartsWithSegments("/fortunes", StringComparison.Ordinal) Then
            Dim sw = Stopwatch.StartNew()
            Dim db = httpContext.RequestServices.GetService(Of RawDb)()
            Dim rows = Await db.LoadFortunesRows()
            Dim x = sw.ElapsedMilliseconds
            Interlocked.Add(DbTime, sw.ElapsedMilliseconds)
            Await MiddlewareHelpers.RenderFortunesHtml(rows, httpContext, Encoder)
            Interlocked.Add(RenderTime, sw.ElapsedMilliseconds - x)
            Return
        End If

        Await NextStage(httpContext)

    End Function
End Class

Module FortunesRawMiddlewareExtensions
    <Extension()>
    Function UseFortunesRaw(ByVal builder As IApplicationBuilder) As IApplicationBuilder
        Return builder.UseMiddleware(Of FortunesRawMiddleware)()
    End Function
End Module
