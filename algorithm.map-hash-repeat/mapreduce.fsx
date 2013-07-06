﻿// BEGIN PREAMBLE -- do not evaluate, for intellisense only
#r "Nessos.MBrace.Utils"
#r "Nessos.MBrace.Actors"
#r "Nessos.MBrace.Base"
#r "Nessos.MBrace.Store"
#r "Nessos.MBrace.Client"

open Nessos.MBrace.Client
// END PREAMBLE

#r "../lib/bin/Debug/Nessos.MBrace.Lib.dll"
open Nessos.MBrace.Lib
open Nessos.MBrace.Lib.MapReduce

// An implementation of the mapReduce function (see wordcount example).
// Uses somewhat dynamic parallelism. It spawns a number of cloud computations proportional to the
// number of the workers, and then creates some async computations (proportional to the number of
// hardware threads on a machine). Finally the execution is sequential.


let noiseWords = 
    seq [
        "a"; "about"; "above"; "all"; "along"; "also"; "although"; "am"; "an"; "any"; "are"; "aren't"; "as"; "at";
        "be"; "because"; "been"; "but"; "by"; "can"; "cannot"; "could"; "couldn't"; "did"; "didn't"; "do"; "does"; 
        "doesn't"; "e.g."; "either"; "etc"; "etc."; "even"; "ever";"for"; "from"; "further"; "get"; "gets"; "got"; 
        "had"; "hardly"; "has"; "hasn't"; "having"; "he"; "hence"; "her"; "here"; "hereby"; "herein"; "hereof"; 
        "hereon"; "hereto"; "herewith"; "him"; "his"; "how"; "however"; "I"; "i.e."; "if"; "into"; "it"; "it's"; "its";
        "me"; "more"; "most"; "mr"; "my"; "near"; "nor"; "now"; "of"; "onto"; "other"; "our"; "out"; "over"; "really"; 
        "said"; "same"; "she"; "should"; "shouldn't"; "since"; "so"; "some"; "such"; "than"; "that"; "the"; "their"; 
        "them"; "then"; "there"; "thereby"; "therefore"; "therefrom"; "therein"; "thereof"; "thereon"; "thereto"; 
        "therewith"; "these"; "they"; "this"; "those"; "through"; "thus"; "to"; "too"; "under"; "until"; "unto"; "upon";
        "us"; "very"; "viz"; "was"; "wasn't"; "we"; "were"; "what"; "when"; "where"; "whereby"; "wherein"; "whether";
        "which"; "while"; "who"; "whom"; "whose"; "why"; "with"; "without"; "would"; "you"; "your" ; "have"; "thou"; "will"; 
        "shall"
    ]
    |> fun words -> new Set<string>(words)


open System
open System.IO
open Nessos.MBrace.Utils

[<Cloud>]
let mapCloudTree (paths : string []) =
    cloud {
        let texts = paths |> Array.map (fun path -> File.ReadAllText (path))
        return! newRef <| Leaf texts
    }
[<Cloud>]
let reduceCloudTree (left : ICloudRef<CloudTree<'T>>) (right : ICloudRef<CloudTree<'T>>) =
    cloud {
        return! newRef <| Branch (left, right)
    }


let mapF (texts : string[]) =
        let words = texts |> Array.map (fun text -> text.Split([|' '; '.'; ','|], StringSplitOptions.RemoveEmptyEntries)) |> Seq.concat
        words
        |> Seq.map (fun word -> word.ToLower())
        |> Seq.map (fun t -> t.Trim())
        |> Seq.filter (fun word -> Seq.length word > 3 && not <| noiseWords.Contains(word) )
        |> Seq.groupBy id
        |> Seq.map (fun (key, values) -> (key, values |> Seq.length))
        |> Seq.sortBy (fun (_,t) -> -t)
        |> Seq.toArray
    


let reduceF (left: (string * int) []) (right: (string * int) []) = 
    Seq.append left right 
    |> Seq.groupBy fst 
    |> Seq.map (fun (key, value) -> (key, value |> Seq.sumBy snd ))
    |> Seq.sortBy (fun (_,t) -> -t)
    |> Seq.toArray


#time

let runtime = MBrace.InitLocal 4
let fileSource = Path.Combine(__SOURCE_DIRECTORY__, @"..\Wikipedia")
let files = Directory.GetFiles(fileSource) |> Seq.toArray

let proc = runtime.CreateProcess <@ mapReduceArray mapCloudTree reduceCloudTree (fun () -> cloud { return! newRef Empty }) files 2 @>
let result = proc.AwaitResult()
let proc' = runtime.CreateProcess <@ mapReduceCloudTree (Cloud.lift mapF) (Cloud.lift2 reduceF) undefined result @>