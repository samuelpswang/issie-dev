﻿module TruthTableUpdate
open SimGraphTypes
open SimTypes
open Helpers
open DiagramStyle
open ModelType
open PopupHelpers
open TruthTableTypes
open TruthTableCreate
open TruthTableReduce
open Elmish

open Fulma
open Fable.React
open Fable.React.Props
//open CatalogueView
open NumberHelpers
open Optics
open Optics.Optic
open Optics.Operators

/// Initial value of model
let tTTypeInit =
    {
        BitLimit = 10
        InputConstraints = TruthTableTypes.emptyConstraintSet
        OutputConstraints = TruthTableTypes.emptyConstraintSet
        HiddenColumns = []
        SortType = None
        IOOrder = [||]
        GridStyles = Map.empty
        GridCache = None
        AlgebraIns = []
        DraggedColumn = None
        HoveredColumn = None
        PrevIOOrder = None
    }


let getTruthTableOrFail model msg =
    match model.CurrentTruthTable with
    | None -> failwithf "what? Getting truth table when no table has been generated: %s" msg
    | Some res ->
        match res with
        | Error _ -> failwithf "what? Getting truth table when there is error in generation: %s" msg
        | Ok tt -> tt

let truncationWarning table =
    $"The Truth Table has been truncated to {table.TableMap.Count} input combinations.
    Not all rows may be shown."

/// Apply a single numerical output constraint to a Truth Table Map
let applyNumericalOutputConstraint (table: Map<TruthTableRow,TruthTableRow>) (con: Constraint) =
    table
    |> Map.filter (fun _ right ->
        right
        |> List.exists (fun cell ->
            match con with
            | Equality e ->
                if e.IO <> cell.IO then
                    false
                else
                    match cell.Data with
                    | Algebra _ -> false
                    | DC -> true
                    | Bits wd ->
                        let cellVal = convertWireDataToInt wd
                        cellVal = bigint e.Value
            | Inequality i ->
                if i.IO <> cell.IO then
                    false
                else
                    match cell.Data with
                    | Algebra _ -> false
                    | DC -> true
                    | Bits wd ->
                        let cellVal = convertWireDataToInt wd
                        i.LowerBound <= int cellVal && cellVal <= bigint i.UpperBound
                        ))

/// Comparison function for CellData values
let compareCellData (cd1: CellData) (cd2: CellData) =
    match cd1, cd2 with
    | DC, DC -> 0 // X = X
    | DC, _ -> 1 // DC is considered larger than anything
    | _, DC -> -1
    | Algebra _, Bits _ -> 1 // Algebra is considered larger than Bits
    | Bits _, Algebra _ -> -1
    | Algebra a1, Algebra a2 -> 
        compare a1 a2 // Algebra compared by alphabetical order
    | Bits wd1, Bits wd2 -> // Bits compared by which is largest
        (convertWireDataToInt wd1, convertWireDataToInt wd2)
        ||> compare

let sortByIO (io: CellIO) (lst: TruthTableRow list) = 
    let idx = 
        lst.Head
        |> List.tryFindIndex (fun c ->
            c.IO = io)
        |> function
            | Some i -> i
            | None -> failwithf "what? Failed to find IO: %s while sorting TT" io.getLabel
    
    lst
    |> List.sortWith (fun r1 r2 ->
        let cd1 = r1[idx].Data
        let cd2 = r2[idx].Data
        compareCellData cd1 cd2)




/// Elmish Update function for Truth Table messages (TTMsg)
let truthTableUpdate (model: Model) (msg:TTMsg) : (Model * Cmd<Msg>)  =

    let withMsg (m:Msg) (model: Model) = (model, Cmd.ofMsg(m))
    let withTTMsg (m:TTMsg) (model: Model) = (model, Cmd.ofMsg(TruthTableMsg m))
    let withCmdNone (model: Model) = (model, Cmd.none) 
    let withCommands (commands: Cmd<Msg> list) (model: Model) = (model, Cmd.batch commands)

    match msg with
    | GenerateTruthTable simRes ->
        match simRes with
        | Some (Ok sd,_) ->
            // delete any old simulations
            let model = ModelHelpers.removeAllSimulationsFromModel model
            // Set all inputs as algebraic by default
            let modelWithAlgebra = 
                model
                |> set (tTType_ >-> algebraIns_) sd.Inputs
            // Generate the Truth Table
            let tt = 
                truthTable 
                    sd 
                    modelWithAlgebra.TTConfig
                    false
            // Styles for the grid
            let colStyles = 
                tt.IOOrder
                |> List.mapi (fun i io -> (io,ttGridColumnProps i))
                |> Map.ofList 
            // List of messages
            let commands = 
                [
                    // Set the IO Order for the Truth Table
                    tt.IOOrder 
                    |> List.toArray 
                    |> SetIOOrder
                    |> TruthTableMsg
                    // Set the popup Algebra inputs to default to all inputs
                    (TruthTableMsg <| SetPopupAlgebraInputs (Some sd.Inputs))
                    // Truncation warning
                    if tt.IsTruncated then
                        Notifications.warningPropsNotification (truncationWarning tt)
                        |> SetPropertiesNotification
                ]
                |> List.map Cmd.ofMsg
            modelWithAlgebra
            |> set currentTruthTable_ (Some (Ok tt))
            |> set (tTType_ >-> gridStyles_) colStyles
            |> withCommands commands
        | Some (Error e, _) ->
            model
            |> set currentTruthTable_ (Some (Error e))
            |> set (tTType_ >-> gridStyles_) Map.empty,
                Cmd.none
        | None -> model, Cmd.none
     
    | RegenerateTruthTable ->
        let table = getTruthTableOrFail model "Regeneration"
        let ttRes, commands =
            try
                let tt =
                    truthTable
                        table.TableSimData
                        model.TTConfig
                        true
                let comms = 
                    [
                        // If table is truncated, issue truncation warning. 
                        if tt.IsTruncated then
                            Notifications.warningPropsNotification (truncationWarning tt)
                            |> SetPropertiesNotification
                        // Else, clear any prior truncation warning.
                        else
                            ClosePropertiesNotification
                        // Filter using output constraints
                        FilterTruthTable |> TruthTableMsg
                    ]
                    |> List.map Cmd.ofMsg
                Ok tt, comms
            with
            // Protections when setting algebraic inputs should mean this never occurs,
            // but leaving this in as a fallback.
            | AlgebraNotImplemented err -> Error err, []
        {model with CurrentTruthTable = Some ttRes}
        |> withCommands commands

    | FilterTruthTable ->
        let table = getTruthTableOrFail model "Refilter"
        let tMap = 
            match table.DCMap with
            | Some m -> m
            | None -> table.TableMap
        let allOutputConstraints =
            (model.TTConfig.OutputConstraints.Equalities
            |> List.map Equality)
            @
            (model.TTConfig.OutputConstraints.Inequalities
            |> List.map Inequality)
        let filteredMap =
            (tMap, allOutputConstraints)
            ||> List.fold applyNumericalOutputConstraint
        let newTable = {table with FilteredMap = filteredMap} |> Ok |> Some
        {model with CurrentTruthTable = newTable}
        |> withTTMsg  SortTruthTable

    | SortTruthTable ->
        let start = TimeHelpers.getTimeMs()
        let table = getTruthTableOrFail model "Sorting"
        let sortedTable =
            match model.TTConfig.SortType, tableAsList table.FilteredMap with 
            | _, [] -> 
                {table with SortedListRep = []}
            | None, lst ->
                {table with SortedListRep = lst}
            | Some (io, Ascending), lst ->
                let sortedLst = sortByIO io lst
                {table with SortedListRep = sortedLst}
            | Some (io, Descending), lst ->
                let sortedLst = 
                    sortByIO io lst
                    |> List.rev
                {table with SortedListRep = sortedLst}
            |> TimeHelpers.instrumentInterval "Sorting Truth Table" start
            |> Ok
            |> Some
        {model with CurrentTruthTable = sortedTable}
        |> withTTMsg HideTTColumns

    | DCReduceTruthTable ->
        let table = getTruthTableOrFail model "DC Reduction"
        let start = TimeHelpers.getTimeMs() 
        let reducedTable = 
            reduceTruthTable table None
            |> TimeHelpers.instrumentInterval "DC Reduction" start
            |> Ok
            |> Some
        {model with CurrentTruthTable = reducedTable}
        |> withTTMsg FilterTruthTable

    | HideTTColumns ->
        let start = TimeHelpers.getTimeMs()
        /// Recursive function to hide columns and adjust the positions of the remaining
        /// visible columns.
        let rec correctProps (index: int) (acc: list<CellIO*list<CSSProp>>) (lst: CellIO list):  list<CellIO*list<CSSProp>>=
            let hiddenProps = ttGridHiddenColumnProps model.TTConfig.IOOrder.Length
            match lst with
            | [] -> acc
            | io::tl ->
                if List.contains io model.TTConfig.HiddenColumns then
                    correctProps (index) ((io,hiddenProps)::acc) tl
                else
                    correctProps (index+1) ((io,ttGridColumnProps index)::acc) tl
        let newStyles =
            correctProps 0 [] (Array.toList model.TTConfig.IOOrder)
            |> Map.ofList
            |> TimeHelpers.instrumentInterval "Hiding Columns" start
        model
        |> set (tTType_ >-> gridStyles_)  newStyles
        |> withTTMsg (SetTTGridCache None)

    | CloseTruthTable -> 
        let newPopupData =
            {model.PopupDialogData with 
                AlgebraInputs = None
                AlgebraError = None
                ConstraintErrorMsg = None}    
            
        { model with
            TTConfig = tTTypeInit
            CurrentTruthTable = None
            PopupDialogData = newPopupData}
        |> withCmdNone

    | ClearInputConstraints ->
        model
        |> set (tTType_ >-> inputConstraints_) emptyConstraintSet
        |> withTTMsg RegenerateTruthTable

    | ClearOutputConstraints ->
        model
        |> set (tTType_ >-> outputConstraints_) emptyConstraintSet
        |> withTTMsg RegenerateTruthTable

    | AddInputConstraint con ->
        match con with
        | Equality e -> 
            model
            |> map (tTType_ >-> inputConstraints_ >-> equalities_) (fun eqs -> e :: eqs)
            |> withTTMsg RegenerateTruthTable
        | Inequality i ->
            model
            |> map (tTType_ >-> inputConstraints_ >-> inequalities_) (fun inEqs -> i :: inEqs)
            |> withTTMsg RegenerateTruthTable

    | DeleteInputConstraint con ->
        match con with
        | Equality e ->
            model
            |> map (tTType_ >-> inputConstraints_ >-> equalities_) (List.except [e])
            |> withTTMsg RegenerateTruthTable
        | Inequality i ->
            model
            |> map (tTType_ >-> inputConstraints_ >-> inequalities_) (List.except [i])
            |> withTTMsg RegenerateTruthTable

    | AddOutputConstraint con ->
        match con with
        | Equality e ->
            model
            |> map (tTType_ >-> outputConstraints_ >-> equalities_) (fun eqs -> e :: eqs)
        | Inequality i ->
            model
            |> map (tTType_ >-> outputConstraints_ >-> inequalities_) (fun inEqs -> i :: inEqs)
        |> withTTMsg FilterTruthTable

    | DeleteOutputConstraint con ->
        match con with
        | Equality e ->
            model
            |> map (tTType_ >-> outputConstraints_ >-> equalities_) (List.except [e])
        | Inequality i ->
            model
            |> map (tTType_ >-> outputConstraints_ >-> inequalities_) (List.except [i])
        |> withTTMsg FilterTruthTable

    | ToggleHideTTColumn io ->
        // Column is currently hidden, so we unhide
        if List.contains io model.TTConfig.HiddenColumns then
            model
            |> map (tTType_ >-> hiddenColumns_) (List.except [io])
        else
            let newSort =
                match model.TTConfig.SortType with
                | None -> None
                | Some (cIO,st) ->
                    if cIO = io then None
                    else Some (cIO,st)
            model
            |> map (tTType_ >-> hiddenColumns_) (fun cols -> io :: cols)
            |> set (tTType_ >-> sortType_) newSort
        |> withTTMsg HideTTColumns
    | ClearHiddenTTColumns -> 
        model
        |> set (tTType_ >-> hiddenColumns_) []
        |> withTTMsg HideTTColumns
    | ClearDCMap ->
        let newTT = 
            match model.CurrentTruthTable with
            | None -> None
            | Some tableopt ->
                match tableopt with
                | Error _ -> failwithf "what? Trying to clear DC Map in TT with error"
                | Ok table ->
                    {table with DCMap = None}
                    |> Ok
                    |> Some
        {model with CurrentTruthTable = newTT}
        |> withTTMsg FilterTruthTable
    | SetTTSortType stOpt ->
        model
        |> set (tTType_ >-> sortType_) stOpt
        |> withTTMsg SortTruthTable
    | MoveColumn (io, dir) ->
        let oldOrder = model.TTConfig.IOOrder
        let idx = 
            oldOrder
            |> Array.tryFindIndex (fun cIO -> cIO = io)
            |> function
                | Some i -> i
                | None -> failwithf "what? IO: %A not found in TTIOOrder" io
        let newOrder =
            match dir, idx with
            | MLeft, 0 -> oldOrder
            | MLeft, i -> swapArrayEls (i) (i-1) oldOrder
            | MRight, i -> 
                if i = (oldOrder.Length-1) then
                    oldOrder
                else
                    swapArrayEls (idx) (idx+1) oldOrder
        let newStyles =
            newOrder
            |> Array.mapi (fun i io -> (io,ttGridColumnProps i))
            |> Map.ofArray
        model
        |> set (tTType_ >-> ioOrder_) newOrder
        |> set (tTType_ >-> gridStyles_) newStyles
        |> withTTMsg (SetTTGridCache None)
    | SetIOOrder x -> 
        model
        |> set (tTType_ >-> ioOrder_) x
        |> withCmdNone
    | SetTTAlgebraInputs lst ->
        model
        |> set (tTType_ >-> algebraIns_) lst
        |> withTTMsg RegenerateTruthTable
    | SetTTBase numBase ->
        let table = getTruthTableOrFail model "SetTTBase"
        let updatedTT = 
            {table with TableSimData = {table.TableSimData with NumberBase = numBase}}
            |> Ok
            |> Some
        {model with CurrentTruthTable = updatedTT}
        |> withTTMsg (SetTTGridCache None)
    | SetTTGridCache gridopt ->
        model
        |> set (tTType_ >-> gridCache_) gridopt
        |> withCmdNone
    
    | StartDraggingColumn column ->
        model
        |> set (tTType_ >-> draggedColumn_) (Some column)
        |> set (tTType_ >-> prevIOOrder_) (Some model.TTConfig.IOOrder)
        |> withCmdNone
    
    | DragColumnEnter targetColumn ->
        match model.TTConfig.DraggedColumn with
        | None -> model |> withCmdNone
        | Some draggedColumn when draggedColumn = targetColumn ->
            // Don't update if entering the same column
            model |> withCmdNone
        | Some draggedColumn ->
            let oldOrder = model.TTConfig.IOOrder
            let draggedIdx = Array.findIndex ((=) draggedColumn) oldOrder
            let targetIdx = Array.findIndex ((=) targetColumn) oldOrder
            
            // Check if both columns are of the same type (input or output)
            let isColumnOutput io =
                match model.CurrentTruthTable with
                | Some (Ok table) ->
                    let inputCount = table.TableSimData.Inputs.Length
                    let ioOrder = Array.toList oldOrder
                    let cellIndex = List.tryFindIndex ((=) io) ioOrder |> Option.defaultValue -1
                    cellIndex >= inputCount && cellIndex < (inputCount + table.TableSimData.Outputs.Length)
                | _ -> false
            
            let draggedIsOutput = isColumnOutput draggedColumn
            let targetIsOutput = isColumnOutput targetColumn
            
            // Only allow swapping if both columns are of the same type
            if draggedIsOutput <> targetIsOutput then
                model
                |> set (tTType_ >-> hoveredColumn_) (Some targetColumn)
                |> withCmdNone
            else
                // Determine if we should swap based on the current state
                let shouldSwap = 
                    match model.TTConfig.HoveredColumn with
                    | Some hoveredCol when hoveredCol = targetColumn ->
                        // Already hovering over this column, don't swap again
                        false
                    | _ ->
                        // Allow swap in all other cases
                        true
                
                if shouldSwap then
                    // Calculate new order by moving dragged column to target position
                    let newOrder =
                        // First, create list without the dragged column
                        let withoutDragged = 
                            oldOrder 
                            |> Array.toList 
                            |> List.filter (fun x -> x <> draggedColumn)
                        
                        // Find where to insert the dragged column
                        // We want to insert it at the target's position
                        let insertIdx =
                            if draggedIdx < targetIdx then
                                // Dragging right: insert after the target
                                min targetIdx (withoutDragged.Length)
                            else
                                // Dragging left: insert before the target
                                targetIdx
                        
                        withoutDragged
                        |> List.insertAt insertIdx draggedColumn
                        |> Array.ofList
                    
                    // Update grid styles for new positions
                    let newStyles =
                        newOrder
                        |> Array.mapi (fun i io -> (io, ttGridColumnProps i))
                        |> Map.ofArray
                    
                    model
                    |> set (tTType_ >-> ioOrder_) newOrder
                    |> set (tTType_ >-> gridStyles_) newStyles
                    |> set (tTType_ >-> hoveredColumn_) (Some targetColumn)
                    |> set (tTType_ >-> gridCache_) None
                    |> withCmdNone
                else
                    model
                    |> set (tTType_ >-> hoveredColumn_) (Some targetColumn)
                    |> withCmdNone
    
    | EndDraggingColumn ->
        model
        |> set (tTType_ >-> draggedColumn_) None
        |> set (tTType_ >-> hoveredColumn_) None
        |> set (tTType_ >-> prevIOOrder_) None
        |> set (tTType_ >-> gridCache_) None  // Force re-render to clear visual state
        |> withCmdNone
    
    | CancelDraggingColumn ->
        // Restore original order if drag was cancelled
        let originalOrder = 
            model.TTConfig.PrevIOOrder 
            |> Option.defaultValue model.TTConfig.IOOrder
        
        let newStyles =
            originalOrder
            |> Array.mapi (fun i io -> (io, ttGridColumnProps i))
            |> Map.ofArray
        
        model
        |> set (tTType_ >-> ioOrder_) originalOrder
        |> set (tTType_ >-> gridStyles_) newStyles
        |> set (tTType_ >-> draggedColumn_) None
        |> set (tTType_ >-> hoveredColumn_) None
        |> set (tTType_ >-> prevIOOrder_) None
        |> set (tTType_ >-> gridCache_) None
        |> withCmdNone
    | TogglePopupAlgebraInput (io,sd) ->
        let (_,_,w) = io
        let oldLst =
            match model.PopupDialogData.AlgebraInputs with
            | Some l -> l
            | None -> failwithf  "what? PopupDialogData.AlgebraInputs is None when trying to toggle"
        if List.contains io oldLst then // Algebra -> Values
            let zero = IData <| convertIntToFastData w 0u
            match ConstraintReduceView.validateAlgebraInput io zero sd with
            | Ok _ ->
                let newLst = List.except [io] oldLst
                model
                |> map popupDialogData_ (
                    set algebraInputs_ (Some newLst) >> 
                    set algebraError_ None
                ), Cmd.none
                
            | Error err ->
                let newLst = List.except [io] oldLst
                model
                |> map popupDialogData_ (
                    set algebraInputs_ (Some newLst) >> 
                    set algebraError_ (Some err)
                ), Cmd.none

        else // Values -> Algebra
            let alg = IAlg <| SingleTerm io
            match ConstraintReduceView.validateAlgebraInput io alg sd with
            | Ok _ ->
                let newLst = io::oldLst
                model
                |> map popupDialogData_ (
                    set algebraInputs_ (Some newLst) >> 
                    set algebraError_ None
                ), Cmd.none
            | Error err ->
                let newLst = io::oldLst
                model
                |> map popupDialogData_ (
                    set algebraInputs_ (Some newLst) >> 
                    set algebraError_ (Some err)
                ), Cmd.none

    | SetPopupConstraintTypeSel ct ->
        set (popupDialogData_ >-> constraintTypeSel_) ct model, Cmd.none

    | SetPopupConstraintIOSel io ->
        set (popupDialogData_ >-> constraintIOSel_) io model, Cmd.none

    | SetPopupConstraintErrorMsg msg ->
        set (popupDialogData_ >-> constraintErrorMsg_) msg model, Cmd.none

    | SetPopupNewConstraint con ->
        set (popupDialogData_ >-> newConstraint_) con model, Cmd.none

    | SetPopupAlgebraInputs opt ->
        set (popupDialogData_ >-> algebraInputs_) opt model, Cmd.none

    | SetPopupAlgebraError opt ->
        set (popupDialogData_ >-> algebraError_) opt model, Cmd.none

    | SetPopupInputConstraints _ | SetPopupOutputConstraints _ ->
        model, Cmd.none




