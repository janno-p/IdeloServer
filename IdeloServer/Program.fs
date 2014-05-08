open MongoDB.Bson
open MongoDB.Bson.IO
open MongoDB.Driver
open MongoDB.Driver.Builders
open MongoDB.FSharp
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Files
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Types
open Suave.Utils
open Suave.Utils.Option
open Suave.Web
open System
open System.IO
open System.Text

Serializers.Register()

let client = MongoClient("mongodb://localhost")
let server = client.GetServer()
let db = server.GetDatabase("idelo")

let jsonSettings = JsonWriterSettings(OutputMode = JsonOutputMode.Strict)

let plain_request f (a : HttpContext) =
    a.request.response.Headers.Add("Content-Type", "text/plain")
    f a.request a

type FieldType =
    | Simple of string
    | Array of string * char

let FieldName = function
    | Simple x -> x
    | Array (x, _) -> x

let FieldToBson field (value : Collections.Generic.Dictionary<string, string>) =
    match field with
    | Simple x -> (BsonString value.[x]) :> BsonValue
    | Array (x, sep) -> (BsonArray (value.[x].Split(sep) |> Array.map (fun s -> s.Trim()) |> Array.filter(fun s -> not (String.IsNullOrWhiteSpace(s))))) :> BsonValue

let ApplySort fields (query : Collections.Generic.Dictionary<string, string>) (values : MongoCursor<BsonDocument>) =
    let sortField =
        let sortName = query ? order |> or_default ""
        match fields |> List.tryFind (fun f -> f = sortName) with
        | Some name -> name
        | _ -> "_id"
    let sortBy = match query ? direction with
                 | Some "asc" -> SortBy.Ascending(sortField)
                 | _ -> SortBy.Descending(sortField)
    values.SetSortOrder(sortBy)

let ApplyPaging fields (query : Collections.Generic.Dictionary<string, string>) (values : MongoCursor<BsonDocument>) =
    let offset = match query ? fromrow with
                 | Some x -> let mutable n = 0
                             match Int32.TryParse(x, &n) with
                             | true -> n
                             | _ -> 0
                 | _ -> 0
    let count = match query ? getrows with
                | Some x -> let mutable n = 0
                            match Int32.TryParse(x, &n) with
                            | true -> n
                            | _ -> 100
                | _ -> 100
    values.SetSkip(offset).SetLimit(count)
                
let result_response (total : int64) (items : BsonValue list) =
    let document = BsonDocument([BsonElement("total", (BsonInt64 total)); BsonElement("items", (BsonArray items))])
    OK (document.ToJson jsonSettings)

let save_request (collectionName : string) (fields : FieldType list) = plain_request (fun req ->
    let existing, missing = fields |> List.partition (fun field -> req.query.ContainsKey(FieldName field) && not (String.IsNullOrWhiteSpace(req.query.[FieldName field])))
    match existing |> List.isEmpty with
    | true -> OK "0"
    | _ ->
        let collection = db.GetCollection collectionName
        match req.query ? id with
        | Some id ->
            let update = existing |> List.fold (fun (builder : UpdateBuilder) field -> builder.Set((FieldName field), (FieldToBson field req.query)))
                                               (missing |> List.fold (fun builder field -> builder.Unset(FieldName field))
                                                                     (UpdateBuilder ()))
            collection.Update(Query.EQ("_id", (BsonObjectId (ObjectId id))), update) |> ignore
            OK id
        | _ ->
            let elements = existing |> List.map (fun field -> BsonElement (FieldName field, (FieldToBson field req.query)))
            let document = BsonDocument ((BsonElement ("created_at", BsonString(DateTime.Now.ToString("yyyy-MM-ddTHH:mm")))) :: elements)
            collection.Insert(document) |> ignore
            OK (document.GetValue("_id").ToJson(jsonSettings))
)

let search_request (collectionName : string) (fields : FieldType list) = plain_request (fun req ->
    let collection = db.GetCollection collectionName
    let fieldNames = fields |> List.choose (fun field -> match field with
                                                         | Simple x -> Some x
                                                         | _ -> None)
    let checkFields = Query.Or(fieldNames |> List.map (fun field -> Query.Exists(field)))
    let queries = fieldNames |> List.fold (fun acc field -> match req.query.ContainsKey field with
                                                            | true -> Query.Matches(field, (BsonRegularExpression (req.query.[field], "i"))) :: acc
                                                            | _ -> acc) []
    let idQuery = match req.query ? id with
                  | Some id -> [Query.EQ("_id", BsonObjectId (ObjectId id))]
                  | _ -> []
    let values = match queries with
                 | [] -> collection.Find(Query.And(checkFields :: idQuery))
                 | _ -> collection.Find(Query.And(checkFields :: (List.concat [idQuery; queries])))
    let sortField =
        let sortName = req.query ? order |> or_default ""
        match fieldNames |> List.tryFind (fun f -> f = sortName) with
        | Some x -> x
        | _ -> "_id"
    let sortBy = match req.query ? direction with
                 | Some "asc" -> SortBy.Ascending(sortField)
                 | _ -> SortBy.Descending(sortField)
    let offset = match req.query ? fromrow with
                 | Some x -> let mutable n = 0
                             match Int32.TryParse(x, &n) with
                             | true -> n
                             | _ -> 0
                 | _ -> 0
    let count = match req.query ? getrows with
                | Some x -> let mutable n = 0
                            match Int32.TryParse(x, &n) with
                            | true -> n
                            | _ -> 100
                | _ -> 100
    let numDocuments = values.Clone().Count();
    let documents = values.SetFields(Fields.Include(("created_at" :: (fields |> List.map (fun f -> FieldName f))) |> List.toArray)).SetSkip(offset).SetLimit(count).SetSortOrder(sortBy)
    let result = BsonDocument([BsonElement("total", (BsonInt64 numDocuments))
                               BsonElement("items", (BsonArray (documents |> Seq.map (fun x -> x :> BsonValue) |> Seq.toList)))])
    OK (result.ToJson jsonSettings)
)

module Kaebus =
    let collection = "complaint"
    let fields = [ Simple("title"); Simple("subject"); Simple("time"); Simple("lat"); Simple("lng"); Array("tags", ','); Simple("description"); Simple("user") ]
    let Otsi = search_request collection fields
    let Salvesta = save_request collection fields

    let Sildid = plain_request (fun req ->
        let collection = db.GetCollection collection

        let pipeline = seq {
            if req.query.ContainsKey("q") then
                yield BsonDocument("$match", BsonDocument("tags", BsonRegularExpression(req.query.["q"], "i")))
            yield BsonDocument("$unwind", BsonString "$tags")
            yield BsonDocument("$group", BsonDocument("_id", BsonString "$tags"))
            yield BsonDocument("$project", BsonDocument("insensitive", BsonDocument("$toLower", BsonString "$_id")))
            if req.query.ContainsKey("q") then
                yield BsonDocument("$match", BsonDocument("insensitive", BsonRegularExpression(req.query.["q"])))
            yield BsonDocument("$sort", BsonDocument("insensitive", BsonInt32 1))
            yield BsonDocument("$project", BsonDocument("_id", BsonInt32 1))
            let count = match req.query ? count with
                        | Some count -> let mutable n = 0
                                        match Int32.TryParse(count, &n) with
                                        | true -> n
                                        | _ -> 10
                        | _ -> 10
            yield BsonDocument("$limit", BsonInt32 count)
        }

        OK (collection.Aggregate(AggregateArgs(Pipeline = pipeline)).ToJson(jsonSettings))
    )

module Kasutaja =
    let collection = "user"
    let fields = [ Simple("username"); Simple("password"); Simple("role"); Simple("name") ]
    let Otsi = search_request collection fields
    let Salvesta = save_request collection fields

module Kodanik =
    let collection = "citizen"
    let fields = [ Simple("name"); Simple("gender"); Simple("birth_date"); Simple("address"); Simple("photo_uri") ]
    let Otsi = search_request collection fields
    let Salvesta = save_request collection fields

    let citizenOrder = ApplySort (fields |> List.map FieldName)
    let citizenPaging = ApplyPaging (fields |> List.map FieldName)

    let MinuSubjektid = plain_request (fun req ->
        match req.query ? user with
        | Some id ->
            let complaints = db.GetCollection Kaebus.collection
            let queryUser = Query.EQ("user", new BsonString(id))
            let query = match req.query ? tags with
                        | Some str -> Query.And(queryUser, Query.All("tags", (str.Split(',') |> Array.map (fun s -> BsonString (s.Trim()) :> BsonValue))))
                        | _ -> queryUser
            let subjects = complaints.Distinct("subject", query)
            let collection = db.GetCollection collection
            let fieldNames = fields |> List.choose (fun f -> match f with | Simple x -> Some x | _ -> None)
            let checkQuery = Query.Or(fields |> List.map (fun f -> Query.Exists(FieldName f)))
            let values = collection.Find(Query.And(checkQuery, Query.In("_id", subjects |> Seq.map (fun x -> (BsonObjectId (ObjectId x.AsString)) :> BsonValue))))
            let totalCount = values.Clone().Count()
            let documents = values.SetFields(Fields.Include(fieldNames |> List.toArray)) |> (citizenPaging req.query) |> (citizenOrder req.query)
            result_response totalCount (documents |> Seq.map (fun x -> x :> BsonValue) |> Seq.toList)
        | _ -> result_response 0L []
    )

let ideloApp : WebPart =
    choose [ GET >>= url "/Kaebus/Otsi" >>= Kaebus.Otsi
             GET >>= url "/Kaebus/Salvesta" >>= Kaebus.Salvesta
             GET >>= url "/Kaebus/Sildid" >>= Kaebus.Sildid
             GET >>= url "/Kasutaja/Otsi" >>= Kasutaja.Otsi
             GET >>= url "/Kasutaja/Salvesta" >>= Kasutaja.Salvesta
             GET >>= url "/Kodanik/Otsi" >>= Kodanik.Otsi
             GET >>= url "/Kodanik/Salvesta" >>= Kodanik.Salvesta
             GET >>= url "/Kodanik/MinuSubjektid" >>= Kodanik.MinuSubjektid
             GET >>= browse
             NOT_FOUND "Found no handlers" ]

let default_mime_types_map = function
    | ".map"
    | ".eot"
    | ".ttf"  -> Some { name = "application/octet-stream"; compression = true }
    | ".svg"  -> Some { name = "image/svg+xml"; compression = true }
    | ".woff" -> Some { name = "application/font-woff"; compression = true }
    | ".css"  -> Some { name = "text/css"; compression = true }
    | ".htm"  -> Some { name = "text/html"; compression = true }
    | ".js"   -> Some { name = "application/x-javascript"; compression = true }
    | _       -> None

let ideloConfig = { default_config with
                        mime_types_map = default_mime_types_map
                        home_folder = Some "/home/janno/Work/Idelo" }

web_server ideloConfig ideloApp
