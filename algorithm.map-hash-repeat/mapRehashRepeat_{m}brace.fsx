﻿//map-rehash-repeat "average computation" in F# - {m}brace

#r "Nessos.MBrace.Utils"
#r "Nessos.MBrace.Actors"
#r "Nessos.MBrace.Base"
#r "Nessos.MBrace.Store"
#r "Nessos.MBrace.Client"

#r "../lib/bin/Debug/Nessos.MBrace.Lib.dll"

open Nessos.MBrace.Client

//id,new value, old value
type Node<'Id,'newV,'oldV> = | N of 'Id*'newV*'oldV

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

//creates num Nodes with random new value (1..10)                   
[<Cloud>]
let createNodes (num : int) = cloud {    
    let rnd = System.Random() 
    let initVals = [| for n in 0 .. num-1 -> 
                        cloud { 
                            //let node = (n,rnd.Next (1,11),0)  //test with f#
                            let node = (n,n*n+1,0)
                            return! MutableCloudRef.New(N(node)) 
                        }
                    |] //|> Array.toList |> seqMap (fun node -> MutableCloudRef.New(N(node)))
    return! Cloud.Parallel initVals
    //return! [| for n in 0 .. num-1 -> (n,rnd.Next (1,11),0) |] |> Array.toList |> seqMap (fun node -> MutableCloudRef.New(N(node)))        
    //test with f#//return! [| for n in 0 .. num-1 -> (n,n*n+1,0) |] |> Array.toList |> seqMap (fun node -> MutableCloudRef.New(N(node)))        
}        

let createNeighbors (nodes : IMutableCloudRef<Node<'Id,'newV,'oldV>> []) (nList : List<int*int>) = 
    [| for n in nList ->
        match n with
            | (parent,neighbor) ->
                (parent,neighbor)
    |] 
    |> Seq.groupBy fst |> Seq.map (fun (key,values) -> (key,values |> Seq.map (fun (k1,v1) -> v1) |> Set.ofSeq)) 
    |> Seq.toArray 
    |> Map.ofArray



//average
let compute (vals : 'a list) = 
    List.sum vals / vals.Length


[<Cloud>]
let rec mapRehashRepeat (nodes : IMutableCloudRef<Node<'Id,'newV,'oldV>> []) 
                        (neighbors : Map<'nodeId,Set<'nodeId>>) 
                        compute isDone finish = cloud {
            
    //get the neighbors REFS of the given node                             
    let getNeighbors (node : IMutableCloudRef<Node<'Id,'newV,'oldV>> ) = cloud {
        let! cloudNode = MutableCloudRef.Read(node)
        match cloudNode with
            | N(id,_,_) when Map.containsKey id neighbors ->                
                return neighbors.[id] |> Set.toList |> List.map (fun id -> nodes.[id])
            | N(id,_,_) -> return []
    } 
    //return! seqMap getNeighbors nodes
   
    //sets the new value of the given node changes to the average. old value is the previous new value
    let changeV (node : IMutableCloudRef<Node<'Id,'newV,'oldV>> ) comp = cloud {
        let! cloudNode = MutableCloudRef.Read(node)
        match cloudNode with        
            |N(id,currentV,oldv)  ->                
                let newData = (id,comp,currentV)                 
                do! MutableCloudRef.Force(node,N(newData))                
    }

    let newVals (node : IMutableCloudRef<Node<'Id,'newV,'oldV>> ) = cloud {
        
        //gets the newv from the given node
        let getNewV (node : IMutableCloudRef<Node<'Id,'newV,'oldV>> ) = cloud {
                let! cloudNode = MutableCloudRef.Read(node)
                match cloudNode with
                    | N(_,newv,_)  ->                 
                        return newv    
        }
        //get neighbors REFS from the given node
        let! neighborRefs = getNeighbors node   
        //concatenate given node's newv with the neighbors' newv and return a list with tuples: (nodeId,newvs)
        let! cloudNode = MutableCloudRef.Read(node)
        match cloudNode with   
            | N(id,_,_) ->
                let! neighborVals = seqMap getNewV neighborRefs 
                let! myVal = getNewV node
                return (id, [myVal] @ neighborVals)
    }
    //the newv of the neighbors
    let vals = [|for node in nodes -> newVals node|] 
    let! results = Cloud.Parallel vals 
    //return results |> Map.ofArray 
    let newVals = results |> Map.ofArray
    let av = [| for node in nodes ->
                cloud {
                    let! cloudNode = MutableCloudRef.Read(node)
                    match cloudNode with   
                    | N(id,_,_) ->
                        let vals = newVals.[id]
                        return (id,compute vals)
                } 
            |]   
    let! averagesArr = Cloud.Parallel av
    let averages = averagesArr |> Map.ofArray
    //for node in (nodes |> List.toArray) do
    for node in nodes do
        let! cloudNode = MutableCloudRef.Read(node)
        match cloudNode with   
            | N(id,_,_) when Map.containsKey id averages ->
                let comp = averages.[id]
                let! ok = isDone node comp
                if not ok then
                    finish := false
                do! changeV node comp    
        
    match !finish with  
        | true -> return nodes          
        | false -> 
            return! mapRehashRepeat nodes neighbors compute isDone (ref true)                                
}
    

let finish = ref true
let runtime = MBrace.InitLocal 4
let nodes = runtime.Run <@ createNodes 6 @>
let neighbors = createNeighbors nodes [(0,1);(1,0);(1,2);(2,1);(1,3);(3,1);(2,4);(4,2);(3,4);(4,3);(4,5);(5,4)]
#time
let result = runtime.Run <@ mapRehashRepeat nodes neighbors compute (fun (node : IMutableCloudRef<Node<'Id,'newV,'oldV>> ) comp -> cloud {
                                                                let! cloudNode = MutableCloudRef.Read(node)
                                                                match cloudNode with 
                                                                    | N(id,currentV,oldV) -> return currentV = comp
                                                                }) finish @>                                                                     


[<Cloud>]
let printCloudNodes (nodes : IMutableCloudRef<Node<'Id,'newV,'oldV>> [])= cloud {
    return! seqMap (fun node -> MutableCloudRef.Read(node)) (nodes |> Array.toList)
}

runtime.Run <@ printCloudNodes nodes @>
runtime.Run <@ printCloudNodes result @>