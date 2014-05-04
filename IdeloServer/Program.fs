open MongoDB.Bson
open MongoDB.Driver
open MongoDB.FSharp
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Types
open Suave.Web

Serializers.Register()

let client = new MongoClient("mongodb://localhost")
let server = client.GetServer()
let db = server.GetDatabase("idelo")

type Document = {
    Id : ObjectId
    Salvestaja : string
    Field00 : string
    Field01 : string
    Field02 : string
    Field03 : string
    Field04 : string
    Field05 : string
    Field06 : string
    Field07 : string
    Field08 : string
    Field09 : string
    Field10 : string
    Field11 : string
    Field12 : string
    Field13 : string
    Field14 : string
    Field15 : string
    Field16 : string
    Field17 : string
    Field18 : string
    Field19 : string
}

let Otsi =
    request (fun req -> OK "Otsi GET")

let Salvesta =
    request (fun req -> OK "Salvesta GET")

let Uuenda =
    request (fun req -> OK "Uuenda GET")

let ideloApp : WebPart =
    choose [
        GET >>= url "/Otsi" >>= Otsi
        GET >>= url "/Salvesta" >>= Salvesta
        GET >>= url "/Uuenda" >>= Uuenda
        NOT_FOUND "Found no handlers"
    ]

web_server default_config ideloApp
