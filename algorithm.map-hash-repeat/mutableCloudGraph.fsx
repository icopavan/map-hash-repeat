﻿#r "Nessos.MBrace.Utils"
#r "Nessos.MBrace.Actors"
#r "Nessos.MBrace.Base"
#r "Nessos.MBrace.Store"
#r "Nessos.MBrace.Client"

#r "../lib/bin/Debug/Nessos.MBrace.Lib.dll"

open Nessos.MBrace.Client

type Node<'T> =  | N of 'T * List<IMutableCloudRef<Node<'T>>>  
type Graph<'T>= | G of List<IMutableCloudRef<Node<'T>>>

[<Cloud>]
let cloudNode node = 
    cloud {     
        return! MutableCloudRef.New(N(node))
    }
   
[<Cloud>]
let createCloudGraph (nodes : IMutableCloudRef<Node<'T>> list) = 
    cloud {
        return! MutableCloudRef.New(G(nodes))
    }

[<Cloud>]
let rec seqMap (f : 'T -> ICloud<'S>) (inputs : 'T list) : ICloud<'S list> =
    cloud {
        match inputs with
        | [] -> return []
        | x :: xs ->
            let! v = f x
            let! vs = seqMap f xs
            return v :: vs
    }

[<Cloud>]
let rec sequence (list:  ICloud<'T> list) : ICloud<'T list> = 
    cloud {   
        match list with 
            | [] -> return List.empty
            | x::xs -> 
                let! v = x
                let! vs = sequence xs
                return v :: vs
    }

[<Cloud>]
let initCloudGraph () = 
    cloud {
        let! res = [| for i in 1..5 ->  (i, []) |] |> Array.toList |> seqMap cloudNode
        return! createCloudGraph res
    }

// create a local-only runtime
let runtime = MBrace.InitLocal 4

let graph = runtime.Run <@ initCloudGraph () @>

[<Cloud>]
let addNeighbor (node1 : IMutableCloudRef<Node<'T>>) (node2 : IMutableCloudRef<Node<'T>>) =
    cloud {
        let! n1 = MutableCloudRef.Read node1
        match n1 with
            | N(id,[]) -> 
                let! _ = MutableCloudRef.Force(node1, N(id,[node2]))
                return! MutableCloudRef.Read(node1)
            | N (id,nList) -> 
                let! _ = MutableCloudRef.Force(node1,N(id,List.append nList [node2]))
                return! MutableCloudRef.Read(node1)        
    }



//5->4
let n_5 = runtime.Run <@ addNeighbor n5Ref n4Ref @>

//3->4
let n_3 = runtime.Run <@ addNeighbor n3Ref n4Ref @>

//2->3
let n_2 = runtime.Run <@ addNeighbor n2Ref n3Ref @>

//1->3
let n_1 = runtime.Run <@ addNeighbor n1Ref n3Ref @>

//1->5
let n_1'= runtime.Run <@ addNeighbor n1Ref n5Ref @>

let result = runtime.Run <@ createCloudGraph [n1Ref;n2Ref;n3Ref;n4Ref;n5Ref] @>


//get neighbors list from graph
[<Cloud>]
let getN (graph : IMutableCloudRef<Graph<'T>>) =   
    cloud {
        let! g = MutableCloudRef.Read(graph)
        match g with 
            | G(mutableNodes) -> return mutableNodes                                            
    }

let mutableNodes = runtime.Run <@ getN result @>

/////////////////////////////PRINT/////////////////////////////////////////////

//return current's node id,neighbors
[<Cloud>]
let getData (node : IMutableCloudRef<Node<'T>>) =     
    cloud {        
        let! nd = MutableCloudRef.Read node
        match nd with 
            | N(id,lst) -> return (id,lst)
    } 
    
//print all nodes and neighbors
let printGr (nodes : List<IMutableCloudRef<Node<'T>>>)  =
    for i in nodes do
        let proc = runtime.CreateProcess <@ getData i @>
        let (id,lst) = proc.AwaitResult()
        printfn "Node: %d" id
        for j in lst do
            let proc' = runtime.CreateProcess <@ getData j @>
            let (id',lst') = proc'.AwaitResult()
            printfn "   Neighbor %d" id'

printGr mutableNodes
//////////////////////////////////////////////////////////////////////////

//////////////////////////////SUM//////////////////////////////////////////
let sum (nodes : List<IMutableCloudRef<Node<'T>>>) =     
    let sums = ref List.empty
    for i in nodes do
        let proc = runtime.CreateProcess <@ getData i @>
        let (id,lst) = proc.AwaitResult()
        let tempS = ref 0
        for j in lst do
            let proc' = runtime.CreateProcess <@ getData j @>
            let (id',lst') = proc'.AwaitResult()
            tempS := !tempS + id'
        //printfn "%d" !tempS
        sums := List.append !sums [(id,!tempS)] 
    sums                                   

sum mutableNodes

///////////////////////////////AVERAGE//////////////////////////////////////
[<Cloud>]
let sendToNeighbor v (neighbors : List<IMutableCloudRef<Node<'T>>>) = 
        cloud {
            let i = (new System.Random()).Next(0, neighbors.Length-1)              
            let! N(id,lst) = MutableCloudRef.Read(neighbors.[i])                
            let! s = MutableCloudRef.Set(neighbors.[i],N(v,lst))        
            return i
        }

let average (nodes : List<IMutableCloudRef<Node<'T>>>) =         
    let safediv x y =
        match y with
            | 0 -> x
            | _ -> x/y
    //let av = ref List.empty
    for i in nodes do
        let proc = runtime.CreateProcess <@ getData i @>
        let (id,lst) = proc.AwaitResult()
        let isEmpty = 
            match lst with
                | [] -> true
                | _ -> false
        let tempS = ref 0
        if isEmpty then
            tempS := 0
        else             
            for j in lst do
                let proc' = runtime.CreateProcess <@ getData j @>
                let (id',lst') = proc'.AwaitResult()
                tempS := !tempS + id'
            //printfn "%d" !tempS
            //let x = runtime.Run <@ sendToNeighbor (safediv !tempS lst.Length) lst @>            //average
            let x = runtime.Run <@ sendToNeighbor !tempS lst @>         //sum   
            printfn "%d" x
            //av := List.append !av [(id,safediv !tempS lst.Length)] 
        //av  
    
//average mutableNodes
average mutableNodes
printGr mutableNodes