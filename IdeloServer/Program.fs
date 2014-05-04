open MongoDB.Bson
open MongoDB.Bson.IO
open MongoDB.Driver
open MongoDB.Driver.Builders
open MongoDB.FSharp
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Types
open Suave.Utils
open Suave.Utils.Option
open Suave.Web
open System.IO
open System.Text

Serializers.Register()

let client = new MongoClient("mongodb://localhost")
let server = client.GetServer()
let db = server.GetDatabase("idelo")

let tables = [| 0 .. 4 |]
             |> Array.map (fun n -> sprintf "t%d" n)

let fields = [| 0 .. 19 |]
             |> Array.map (fun n -> sprintf "t%d" n)
             |> Array.append [| "id"; "salvestaja" |]

let Otsi = request (fun req ->
    req.response.Headers.Add("Content-Type", "text/plain")
    let tableName = match req.query ? table with
                    | Some name -> tables |> Array.tryFind (fun x -> x = name)
                    | _ -> None
    match tableName with
    | None -> OK "0"
    | Some tableName ->
        let collection = db.GetCollection tableName
        let queries = fields |> Array.fold (fun acc f -> match req.query.ContainsKey f with
                                                         | true -> Query.EQ(f, new BsonString(req.query.[f])) :: acc
                                                         | false -> acc) []
        let values = match queries with
                     | [] -> collection.FindAll()
                     | _ -> collection.Find(Query.And(queries))
        let sortField =
            let sortName = req.query ? order |> or_default "id"
            match fields |> Array.find (fun f -> f = sortName) with
            | "id" -> "_id"
            | x -> x
        let sortBy = match req.query ? direction with
                     | Some "asc" -> SortBy.Ascending(sortField)
                     | _ -> SortBy.Descending(sortField)
        let settings = new JsonWriterSettings(OutputMode = JsonOutputMode.Strict)
        let offset = match req.query ? fromrow with
                     | Some x -> let mutable n = 0
                                 match System.Int32.TryParse(x, &n) with
                                 | true -> n
                                 | _ -> 0
                     | _ -> 0
        let count = match req.query ? getrows with
                    | Some x -> let mutable n = 0
                                match System.Int32.TryParse(x, &n) with
                                | true -> n
                                | _ -> 100
                    | _ -> 100
        OK (values.SetSkip(offset).SetLimit(count).SetSortOrder(sortBy).ToJson(settings))
)

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
