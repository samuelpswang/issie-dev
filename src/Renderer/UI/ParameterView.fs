﻿module ParameterView

open ParameterTypes
open EEExtensions
open VerilogTypes
open Fulma
open Fable.React
open Fable.React.Props

open JSHelpers
open ModelType
open CommonTypes
open PopupHelpers
open Sheet.SheetInterface
open DrawModelType
open Optics
open Optics.Operators
open Optic
open System.Text.RegularExpressions
open Fulma.Extensions.Wikiki

//------------------------------------------------------------------------------------------------
//------------------------------ Handle parameters defined on design sheets ----------------------
//------------------------------------------------------------------------------------------------

(*
 * Parameters are symbols defined constant values that can be used in the design.
 * Parameter definitions have integer default values given in the sheet definition (properties pane).
 * These can be over-ridden per instance by definitions in the component instance (properties pane).
 * Parameter values can in general be defined using parameter expressions containing in-scope parameters
 * Parameter scope is currently defined to be all component instances on the parameter sheet.
 * Parameters are used in parameter expressions in the properties pane of components.
 *
 * See Common/parameterTypes.fs for the types used to represent parameters and parameter expressions.
 *)



// Lenses & Prisms for accessing sheet parameter information


let lcParameterInfoOfModel_ = openLoadedComponentOfModel_ >?> lcParameterSlots_ 
let paramSlotsOfModel_ = lcParameterInfoOfModel_ >?> paramSlots_
let defaultBindingsOfModel_ = lcParameterInfoOfModel_ >?> defaultBindings_

let modelToSymbols = sheet_ >-> SheetT.wire_ >-> BusWireT.symbol_ >-> SymbolT.symbols_

let symbolsToSymbol_ (componentId: ComponentId): Optics.Lens<Map<ComponentId, SymbolT.Symbol>, SymbolT.Symbol> =
    Lens.create
        (fun symbols -> 
            match Map.tryFind componentId symbols with
            | Some symbol -> symbol
            | None -> failwithf "Component %A not found in this sheet" componentId)
        (fun symbol symbols -> 
            symbols |> Map.add componentId symbol)


let symbolToComponent_ : Optics.Lens<SymbolT.Symbol, Component> =
    Lens.create
        (fun symbol -> symbol.Component)
        (fun newComponent symbol -> { symbol with Component = newComponent })


let compSlot_ (compSlotName:CompSlotName) : Optics.Lens<Component, int> = 
    Lens.create
        (fun comp ->
            match compSlotName with
            | Buswidth -> 
                match comp.Type with
                | Viewer busWidth -> busWidth
                | BusCompare1 (busWidth, _, _) -> busWidth
                | BusSelection (outputWidth, _) -> outputWidth
                | Constant1 (width, _, _) -> width
                | NbitsAdder busWidth -> busWidth
                | NbitsAdderNoCin busWidth -> busWidth
                | NbitsAdderNoCout busWidth -> busWidth
                | NbitsAdderNoCinCout busWidth -> busWidth
                | NbitsXor (busWidth, _) -> busWidth
                | NbitsAnd busWidth -> busWidth
                | NbitsNot busWidth -> busWidth
                | NbitsOr busWidth -> busWidth
                | NbitSpreader busWidth -> busWidth
                | SplitWire busWidth -> busWidth
                | Register busWidth -> busWidth
                | RegisterE busWidth -> busWidth
                | Counter busWidth -> busWidth
                | CounterNoLoad busWidth -> busWidth
                | CounterNoEnable busWidth -> busWidth
                | CounterNoEnableLoad busWidth -> busWidth
                | Shift (busWidth, _, _) -> busWidth
                | BusCompare (busWidth, _) -> busWidth
                | Input busWidth -> busWidth
                | Constant (width, _) -> width
                | _ -> failwithf $"Invalid component {comp.Type} for buswidth"
            | NGateInputs ->
                match comp.Type with
                | GateN (_, n) -> n
                | _ -> failwithf $"Invalid component {comp.Type} for gate inputs"
            | IO _ ->
                match comp.Type with
                | Input1 (busWidth, _) -> busWidth
                | Output busWidth -> busWidth
                | _ -> failwithf $"Invalid component {comp.Type} for IO"
            | CustomCompParam paramName ->
                match comp.Type with
                | Custom customComp ->
                    // Look up the parameter value from the custom component's parameter bindings
                    match customComp.ParameterBindings with
                    | Some bindings ->
                        match Map.tryFind (ParamName paramName) bindings with
                        | Some (PInt value) -> value
                        | _ -> failwithf $"Parameter {paramName} not found in custom component {customComp.Name} bindings"
                    | None -> failwithf $"No parameter bindings found for custom component {customComp.Name}"
                | _ -> failwithf $"CustomCompParam can only be used with Custom components, not {comp.Type}"
        )
        (fun value comp->
                let newType = 
                    match compSlotName with
                    | Buswidth ->
                        match comp.Type with
                        | Viewer _ -> Viewer value
                        | BusCompare1 (_, compareValue, dialogText) -> BusCompare1 (value, compareValue, dialogText)
                        | BusSelection (_, outputLSBit) -> BusSelection (value, outputLSBit)
                        | Constant1 (_, constValue, dialogText) -> Constant1 (value, constValue, dialogText)
                        | NbitsAdder _ -> NbitsAdder value
                        | NbitsAdderNoCin _ -> NbitsAdderNoCin value
                        | NbitsAdderNoCout _ -> NbitsAdderNoCout value
                        | NbitsAdderNoCinCout _ -> NbitsAdderNoCinCout value
                        | NbitsXor (_, arithmeticOp) -> NbitsXor (value, arithmeticOp)
                        | NbitsAnd _ -> NbitsAnd value
                        | NbitsNot _ -> NbitsNot value
                        | NbitsOr _ -> NbitsOr value
                        | NbitSpreader _ -> NbitSpreader value
                        | SplitWire _ -> SplitWire value
                        | Register _ -> Register value
                        | RegisterE _ -> RegisterE value
                        | Counter _ -> Counter value
                        | CounterNoLoad _ -> CounterNoLoad value
                        | CounterNoEnable _ -> CounterNoEnable value
                        | CounterNoEnableLoad _ -> CounterNoEnableLoad value
                        | Shift (_, shifterWidth, shiftType) -> Shift (value, shifterWidth, shiftType)
                        | BusCompare (_, compareValue) -> BusCompare (value, compareValue)
                        | Input _ -> Input value
                        | Constant (_, constValue) -> Constant (value, constValue)
                        | _ -> failwithf $"Invalid component {comp.Type} for buswidth"
                    | NGateInputs ->
                        match comp.Type with
                        | GateN (gateType, _) -> GateN (gateType, value)
                        | _ -> failwithf $"Invalid component {comp.Type} for gate inputs"
                    | IO _ ->
                        match comp.Type with
                        | Input1 (_, defaultValue) -> Input1 (value, defaultValue)
                        | Output _ -> Output value
                        | _ -> failwithf $"Invalid component {comp.Type} for IO"
                    | CustomCompParam paramName ->
                        match comp.Type with
                        | Custom customComp ->
                            // Update the parameter value in the custom component's bindings
                            let newBindings = 
                                match customComp.ParameterBindings with
                                | Some bindings -> Map.add (ParamName paramName) (PInt value) bindings
                                | None -> Map.ofList [(ParamName paramName, PInt value)]
                            Custom { customComp with ParameterBindings = Some newBindings }
                        | _ -> failwithf $"CustomCompParam can only be used with Custom components, not {comp.Type}"
                { comp with Type = newType}
)


/// Return a Lens that can be used to read or update the value of a component slot integer in the component.
/// The value is contained in the ComponentType part of a Component record.
/// The Component record will be found in various places, depending on the context.
/// For Properties changes, the Component record will be in the Model under SelectedComponent.
/// For changes in a newly created component the component is created by CatalogueView.createComponent.
/// A partial implementation of this function would be OK for MVP.
/// NB - the Lens cannot be part of the slot record because the Lens type can change depending on 'PINT.
/// Maybe this will be fixed by using a D.U. for the slot type: however for MVP
/// we can simplify things by dealing only with int parameters.
let modelToSlot_ (slot: ParamSlot) : Optics.Lens<Model, int> =
    modelToSymbols
    >-> symbolsToSymbol_ (ComponentId slot.CompId)
    >-> symbolToComponent_
    >-> compSlot_ slot.CompSlot


// evaluateParamExpression, renderParamExpression, parseExpression, and exprContainsParams
// have been moved to ParameterTypes module 


/// Evaluates a list of constraints got from slots against a set of parameter bindings to
/// check what values of param are allowed.
/// NB here 'PINT is not a polymorphic type but a type parameter that will be instantiated to int or bigint.
let evaluateConstraints
        (paramBindings: ParamBindings)
        (exprSpecs: ConstrainedExpr list)
        (dispatch: Msg -> unit)
            : Result<Unit, ParamConstraint list> =


    let failedConstraints konst expr =
        let resultExpression = ParameterTypes.evaluateParamExpression paramBindings expr
        match resultExpression with
            | Ok value ->        
                konst
                |> List.filter (fun constr ->
                    match constr with
                    | MaxVal (expr, errorMsg) -> 
                        match ParameterTypes.evaluateParamExpression paramBindings expr with
                        | Ok maxValue -> value > maxValue
                        | Error err -> // evaluation of constraint failed
                            let errMsg = sprintf "Expression Evaluation of Constraint failed because %s" (string err)
                            dispatch <| SetPopupDialogText (Some (string errMsg))
                            false
                    | MinVal (expr, _) -> 
                        match ParameterTypes.evaluateParamExpression paramBindings expr with
                        | Ok minValue -> value < minValue
                        | Error err -> // evaluation of constraint failed
                            let errMsg = sprintf "Expression Evaluation of Constraint failed because %s" (string err)
                            dispatch <| SetPopupDialogText (Some (string errMsg))
                            false
                    )
            | Error err ->
                let errMsg = sprintf "Expression Evaluation of Constraint failed because %s" (string err)
                dispatch <| SetPopupDialogText (Some (string errMsg))
                List.empty
    
    let result =
        exprSpecs
        |> List.collect (fun slot ->
            failedConstraints slot.Constraints slot.Expression)
    
    if List.isEmpty result then Ok()
    else Error result


/// Generates a ParameterExpression from input text
/// Operators are left-associative
// parseExpression has been moved to ParameterTypes module


/// Get LoadedComponent for currently open sheet
/// This cannot fail, because LoadedComponent must be loaded for sheet to be open
let getCurrentSheet model = 
    let sheetName = 
        match model.CurrentProj with
        | Some proj -> proj.OpenFileName
        | None -> failwithf "Cannot find sheet because no project is open"

    model
    |> ModelHelpers.tryGetLoadedComponents
    |> List.tryFind (fun lc -> lc.Name = sheetName)
    |> function
       | Some lc -> lc
       | None -> failwithf "No loaded component with same name as open sheet"


/// Get default parameter bindings for LoadedComponent 
let getDefaultParams loadedComponent =
    match loadedComponent.LCParameterSlots with
    | Some paramSlots -> paramSlots.DefaultBindings
    | None -> Map.empty


/// Get default parameter slots for LoadedComponent 
let getParamSlots loadedComponent =
    match loadedComponent.LCParameterSlots with
    | Some sheetinfo -> sheetinfo.ParamSlots
    | None -> Map.empty


/// Get current loaded component parameter info
/// Returns empty maps for ParamSlots and DefaultBindings if None
let getLCParamInfo (model: Model) =
    model
    |> get lcParameterInfoOfModel_
    |> Option.defaultValue {ParamSlots = Map.empty; DefaultBindings = Map.empty}

/// Update a custom component's input/output label widths based on parameter evaluations
let updateCustomComponent (labelToEval: Map<string, int>) (newBindings: ParamBindings) (comp: Component) : Component =
    let updateLabels labels =
        labels |> List.map (fun (label, width) ->
            match Map.tryFind label labelToEval with
            | Some newWidth when newWidth <> width -> (label, newWidth) // Update width if changed
            | _ -> (label, width) // Keep the same if unchanged
        )
    
    match comp.Type with
    | Custom customComponent ->
        let updatedCustom = { customComponent with 
                                    InputLabels = updateLabels customComponent.InputLabels
                                    OutputLabels = updateLabels customComponent.OutputLabels
                                    ParameterBindings = Some newBindings }
        { comp with Type = Custom updatedCustom }
    | _ -> comp

/// Use sheet component update functions to perform updates
let updateComponent dispatch model slot value =
    let sheetDispatch sMsg = dispatch (Sheet sMsg)

    let comp = model.Sheet.GetComponentById <| ComponentId slot.CompId
    let compId = ComponentId comp.Id

    // Update component slot value
    match slot.CompSlot with
    | Buswidth | IO _ -> model.Sheet.ChangeWidth sheetDispatch compId value 
    | NGateInputs -> 
        match comp.Type with
        | GateN (gateType, _) -> model.Sheet.ChangeGate sheetDispatch compId gateType value
        | _ -> failwithf $"Gate cannot have type {comp.Type}"
    | CustomCompParam paramName ->
        // For custom component parameters, we need to update the parameter bindings
        match comp.Type with
        | Custom customComp ->
            let newBindings = 
                match customComp.ParameterBindings with
                | Some bindings -> Map.add (ParamName paramName) (PInt value) bindings
                | None -> Map.ofList [(ParamName paramName, PInt value)]
            
            // Get the custom component's loaded component to find parameter slot definitions
            match model.CurrentProj with
            | Some project ->
                let currentSheet = 
                    project.LoadedComponents
                    |> List.tryFind (fun lc -> lc.Name = customComp.Name)
                
                // Calculate updated label widths based on parameter evaluations
                let labelToEval = 
                    match currentSheet with
                    | Some sheet ->
                        match sheet.LCParameterSlots with
                        | Some sheetInfo ->
                            sheetInfo.ParamSlots
                            |> Map.toSeq
                            |> Seq.choose (fun (paramSlot, constrainedExpr) -> 
                                match paramSlot.CompSlot with
                                | IO label -> 
                                    let evaluatedValue = 
                                        match ParameterTypes.evaluateParamExpression newBindings constrainedExpr.Expression with
                                        | Ok expr -> expr
                                        | Error _ -> 0
                                    Some (label, evaluatedValue)
                                | _ -> None 
                            )
                            |> Map.ofSeq
                        | None -> Map.empty
                    | None -> Map.empty
                
                // Update the custom component with new parameter bindings and updated port widths
                let updatedCustom = updateCustomComponent labelToEval newBindings comp
                dispatch <| Sheet (SheetT.Wire (BusWireT.Symbol (SymbolT.ChangeCustom (compId, comp, updatedCustom.Type))))
            | None ->
                // Fallback to just updating bindings if no project context
                let newCustomComp = { customComp with ParameterBindings = Some newBindings }
                dispatch <| Sheet (SheetT.Wire (BusWireT.Symbol (SymbolT.ChangeCustom (compId, comp, Custom newCustomComp))))
        | _ -> failwithf $"CustomCompParam can only be used with Custom components"

    // Update most recent bus width
    match slot.CompSlot, comp.Type with
    | Buswidth, SplitWire _ | Buswidth, BusSelection _ | Buswidth, Constant1 _ -> ()
    | Buswidth, _ | IO _, _ -> dispatch <| ReloadSelectedComponent value
    | _ -> ()


// exprContainsParams has been moved to ParameterTypes module


/// Adds or updates a parameter slot in loaded component param slots
/// Removes the entry if the expression does not contain parameters
let updateParamSlot
    (slot: ParamSlot)
    (exprSpec: ConstrainedExpr)
    (model: Model)
    : Model = 

    let paramSlots = 
        model
        |> get paramSlotsOfModel_
        |> Option.defaultValue Map.empty

    let newParamSlots =
        match ParameterTypes.exprContainsParams exprSpec.Expression with
        | true  -> Map.add slot exprSpec paramSlots
        | false -> Map.remove slot paramSlots

    set paramSlotsOfModel_ newParamSlots model


/// Add the parameter information from a newly created component to paramSlots
let addParamComponent
    (newCompSpec: NewParamCompSpec)
    (dispatch: Msg -> Unit)
    (compId: CommonTypes.ComponentId)
    : Unit =

    let compIdStr =
        match compId with
        | ComponentId txt -> txt
    
    let slot = {CompId = compIdStr; CompSlot = newCompSpec.CompSlot}
    let exprSpec = {
        Expression = newCompSpec.Expression
        Constraints = newCompSpec.Constraints
    }

    updateParamSlot slot exprSpec |> UpdateModel |> dispatch


/// Create a generic input field which accepts and parses parameter expressions
/// Validity of inputs is checked by parser
/// Specific constraints can be passed by callee
let paramInputField
    (model: Model)
    (prompt: string)
    (defaultValue: int)
    (currentValue: Option<int>)
    (constraints: ParamConstraint list)
    (comp: Component option)
    (compSlotName: CompSlotName)
    (dispatch: Msg -> unit)
    : ReactElement =

    let onChange inputExpr = 
        let paramBindings =
            model
            |> get defaultBindingsOfModel_
            |> Option.defaultValue Map.empty

        // Only return first violated constraint
        let checkConstraints expr =
            let exprSpec = {Expression = expr; Constraints = constraints}
            match evaluateConstraints paramBindings [exprSpec] dispatch with
            | Ok () -> Ok ()
                // Error (ParameterTypes.renderParamExpression expr)
            | Error (firstConstraint :: _) ->
                match firstConstraint with
                | MinVal (_, err) | MaxVal (_, err) -> Error err 
            | Error _ -> failwithf "Cannot have error list with no elements"

        let exprResult = ParameterTypes.parseExpression inputExpr
        let newVal = Result.bind (ParameterTypes.evaluateParamExpression paramBindings) exprResult
        let constraintCheck = Result.bind checkConstraints exprResult

        // Either update component or prepare creation of new component
        let useExpr expr value =
            // Update PopupDialogInfo for new component creation and error messages
            let newCompSpec = {
                CompSlot = compSlotName;
                Expression = expr;
                Constraints = constraints;
                Value = value;
            }
            dispatch <| AddPopupDialogParamSpec (compSlotName, Ok newCompSpec)
            match comp with
            | Some c ->
                // Update existing component
                let exprSpec = {Expression = expr; Constraints = constraints}
                let slot = {CompId = c.Id; CompSlot = compSlotName}
                updateComponent dispatch model slot value
                dispatch <| UpdateModel (updateParamSlot slot exprSpec)
            | None -> ()

        match newVal, constraintCheck, exprResult with
        | Ok value, Ok (), Ok expr -> useExpr expr value
        | Error err, _, _ 
        | _, Error err, _ -> dispatch <| AddPopupDialogParamSpec (compSlotName, Error err)
        | _ -> failwithf "Value cannot exist with invalid expression"

    let slots = model |> getCurrentSheet |> getParamSlots
    let inputString = 
        match comp with
        | Some c ->
            let key = {CompId = c.Id; CompSlot = compSlotName}
            if Map.containsKey key slots then
                ParameterTypes.renderParamExpression slots[key].Expression 0 // Or: Some (Map.find key slots)
            else
                currentValue |> Option.defaultValue defaultValue |> string
        | None -> currentValue |> Option.defaultValue defaultValue |> string
    
    let errText = 
        model.PopupDialogData.DialogState
        |> Option.defaultValue Map.empty
        |> Map.tryFind compSlotName
        |> Option.map (
            function
            | Ok _ -> "" 
            | Error err -> err
        )
        |> Option.defaultValue ""

    // Field name, input box, and potential error message
    Field.div [] [
        Label.label [] [str prompt]
        Field.div [Field.Option.HasAddons] [
            Control.div [] [
                Input.text [
                    if errText <> "" then
                        Input.Option.CustomClass "is-danger"
                    Input.Props [
                        OnPaste preventDefault
                        SpellCheck false
                        Name prompt
                        AutoFocus true
                        Style [Width "200px"]
                    ]
                    Input.DefaultValue <| inputString
                    Input.Type Input.Text
                    Input.OnChange (getTextEventValue >> onChange)
                ]
            ]
            if currentValue.IsSome && string currentValue.Value <> inputString then
                Control.p [] [
                    Button.a [Button.Option.IsStatic true] [
                        str (string currentValue.Value)
                    ]
                ]
        ]
        p [Style [Color Red]] [str errText]
    ]


/// Update the values of all parameterised components with a new set of bindings
/// This can only be called after the validity and constraints of all
/// expressions are checked
let updateComponents
    (newBindings: ParamBindings)
    (model: Model)
    (dispatch: Msg -> Unit)
    : Unit =

    let evalExpression expr =
        match ParameterTypes.evaluateParamExpression newBindings expr with
        | Ok value -> value
        | Error _ ->  failwithf "Component update cannot have invalid expression"

    model
    |> get paramSlotsOfModel_
    |> Option.defaultValue Map.empty
    |> Map.map (fun _ expr -> evalExpression expr.Expression)
    |> Map.iter (updateComponent dispatch model)
    

/// Updates the LCParameterSlots DefaultParams section.
type UpdateInfoSheetChoise = 
    | DefaultParams of string * int * bool
    | ParamSlots of ParamSlot * ParameterTypes.ParamExpression * ParamConstraint list


let updateInfoSheetDefaultParams (currentSheetInfo:option<ParameterTypes.ParameterDefs>) (paramName: string) (value: int) (delete:bool)=
    if delete then
        match currentSheetInfo with
        | Some infoSheet -> 
            let newDefaultParams = infoSheet.DefaultBindings |> Map.remove (ParamName paramName)
            let currentSheetInfo = {infoSheet with DefaultBindings = newDefaultParams}
            Some currentSheetInfo
        | None -> None
    else
    match currentSheetInfo with
    | Some infoSheet -> 
        let newDefaultParams = infoSheet.DefaultBindings|> Map.add (ParamName paramName) (PInt value)
        let currentSheetInfo = {infoSheet with DefaultBindings = newDefaultParams}
        Some currentSheetInfo
    | None -> 
        let currentSheetInfo = {DefaultBindings= Map.ofList [(ParamName paramName, PInt value)]; ParamSlots= Map.empty}
        Some currentSheetInfo


let updateInfoSheetParamSlots (currentSheetInfo:option<ParameterTypes.ParameterDefs>) (paramSlot: ParameterTypes.ParamSlot) (expression: ParameterTypes.ParamExpression) (constraints: ParameterTypes.ParamConstraint list) =
    match currentSheetInfo with
    | Some infoSheet -> 
        let newParamSlots = infoSheet.ParamSlots |> Map.add paramSlot {Expression = expression; Constraints = constraints}
        let currentSheetInfo = {infoSheet with ParamSlots = newParamSlots}
        Some currentSheetInfo
    | None -> 
        let currentSheetInfo = {DefaultBindings= Map.empty; ParamSlots = Map.ofList [paramSlot, {Expression = expression; Constraints = constraints}]}
        Some currentSheetInfo


let updateParameter (project: CommonTypes.Project) (model: Model) =
    {model with CurrentProj = Some project}


let getParamsSlot (currentSheet: CommonTypes.LoadedComponent) =
    let getter = CommonTypes.lcParameterSlots_ >?> ParameterTypes.paramSlots_
    match currentSheet.LCParameterSlots with
    | Some _ -> currentSheet ^. getter
    | None -> None


/// This function can be used to update the DefaultParams or ParamSlots in the LCParameterSlots of a sheet based on the choise
/// Use case will be either when we want to add, edit or delete the sheet parameter or when we want to add a new component to the sheet
let modifyInfoSheet (project: CommonTypes.Project) (choise: UpdateInfoSheetChoise) dispatch=
    
    let currentSheet = project.LoadedComponents
                                   |> List.find (fun lc -> lc.Name = project.OpenFileName)
    let updatedSheet = {currentSheet with LCParameterSlots = 
                                                        match choise with
                                                            | DefaultParams (paramName, value, delete) -> updateInfoSheetDefaultParams currentSheet.LCParameterSlots paramName value delete
                                                            | ParamSlots (paramSlot, expression, constraints) -> updateInfoSheetParamSlots currentSheet.LCParameterSlots paramSlot expression constraints}
    let updatedComponents = project.LoadedComponents
                            |> List.map (
                                fun lc ->
                                    if lc.Name = project.OpenFileName
                                    then updatedSheet
                                    else lc
                                )
    let newProject = {project with LoadedComponents = updatedComponents}
    updateParameter newProject |> UpdateModel |> dispatch

/// Creates a popup that allows a parameter integer value to be added.
let addParameterBox model dispatch =
    match model.CurrentProj with
    | None -> JSHelpers.log "Warning: testAddParameterBox called when no project is currently open"
    | Some project ->
        // Prepare dialog popup.
        let title = "Set parameter value"

        let textPrompt =
            fun _ ->
                div []
                    [
                        str "Specify the parameter name:"
                        br []
                        //str $"(current value is {model.ParameterValue})"
                    ]

        let intPrompt =
            fun _ ->
                div []
                    [
                        str "New value for the parameter:"
                        br []
                        //str $"(current value is {model.ParameterValue})"
                    ]

        let defaultVal = 1
        let body = dialogPopupBodyTextAndInt textPrompt "example: x" intPrompt defaultVal dispatch
        let buttonText = "Set value"

        // Update the parameter value then close the popup
        let buttonAction =
            fun (model': Model) -> 
                let newParamName = getText model'.PopupDialogData
                let newValue = getInt model'.PopupDialogData

                modifyInfoSheet (project) (DefaultParams (newParamName, newValue, false)) dispatch
                // Close popup window
                ClosePopup |> dispatch

        // Parameter Names can only be made out of letters and numbers
        let isDisabled = 
            fun (model': Model) -> 
                 let newParamName =  getText model'.PopupDialogData
                 not (Regex.IsMatch(newParamName, "^[a-zA-Z0-9]+$"))

        dialogPopup title body buttonText buttonAction isDisabled [] dispatch

/// Creates a popup that allows a parameter integer value to be edited.
/// TODO: this should be a special cases of a more general popup for parameter expressions?
let editParameterBox model parameterName dispatch   = 
    match model.CurrentProj with
    | None -> JSHelpers.log "Warning: testEditParameterBox called when no project is currently open"
    | Some project ->
        // Prepare dialog popup.
        let currentSheet = project.LoadedComponents
                                   |> List.find (fun lc -> lc.Name = project.OpenFileName)
        let title = "Edit parameter value"
        let currentValue = getDefaultParams currentSheet |> Map.find (ParamName parameterName)
        let intPrompt = 
            fun _ ->
                div []
                    [
                        str $"New value for the parameter {parameterName}:"
                        br []
                        str $"(current value: {currentValue})"
                    ]

        let defaultVal =
            match currentValue with
            | PInt intVal -> intVal
            | _ -> failwithf "Edit parameter box only supports integer bindings"

        let body = dialogPopupBodyOnlyInt intPrompt defaultVal dispatch
        let buttonText = "Set value"

        // Update the parameter value then close the popup
        let buttonAction =
            fun (model': Model) -> 
                let newParamName =  parameterName 
                let newValue = getInt model'.PopupDialogData
                modifyInfoSheet project (DefaultParams (newParamName,newValue,false)) dispatch
                let newBindings =
                    model'
                    |> getLCParamInfo
                    |> (fun info -> info.DefaultBindings)
                    |> Map.add (ParamName newParamName) (PInt newValue) 

                // Value must meet constraints if able to click button
                updateComponents newBindings model dispatch 
                dispatch <| ClosePopup

        // Disabled if any constraints are violated
        let isDisabled = 
            fun (model': Model) ->
                let newParamName =  parameterName 
                let newValue = getInt model'.PopupDialogData
                let newBindings =
                    model'
                    |> getLCParamInfo 
                    |> (fun info -> info.DefaultBindings)
                    |> Map.add (ParamName newParamName) (PInt newValue) 

                let exprSpecs = 
                    model'
                    |> get paramSlotsOfModel_
                    |> Option.defaultValue Map.empty
                    |> Map.toList
                    |> List.map snd

                evaluateConstraints newBindings exprSpecs dispatch
                |> Result.isError

        dialogPopup title body buttonText buttonAction isDisabled [] dispatch


let deleteParameterBox model parameterName dispatch  = 
    match model.CurrentProj with
    | None -> JSHelpers.log "Warning: testDeleteParameterBox called when no project is currently open"
    | Some project ->
        modifyInfoSheet (project) (DefaultParams(parameterName,0,true)) dispatch


/// UI to display and manage parameters for a design sheet.
/// TODO: add structural abstraction.
let private makeParamsField model (comp:LoadedComponent) dispatch =
    let sheetDefaultParams = getDefaultParams comp
    match sheetDefaultParams.IsEmpty with
    | true ->
        div [] [
            Label.label [] [ str "Parameters" ]
            p [] [str "No parameters have been added to this sheet." ]   
            br [] 
            Button.button 
                            [ Fulma.Button.OnClick(fun _ -> addParameterBox model dispatch)
                              Fulma.Button.Color IsInfo
                            ] 
                [str "Add Parameter"]
            ]
    | false ->
    
        div [] [
            Label.label [] [str "Parameters"]
            p [] [str "These parameters have been added to this sheet." ]
            br []
            Table.table [
                        Table.IsBordered
                        Table.IsNarrow
                        Table.IsStriped
                        ] [
                thead [] [
                    tr [] [
                        th [] [str "Parameter"]
                        th [] [str "Value"]
                        th [] [str "Action"]
                    ]
                ]
                tbody [] (
                    sheetDefaultParams |> Map.toList |> List.map (fun (key, value) ->
                        let paramName =
                            match key with 
                            | ParameterTypes.ParamName s -> s
                        let paramVal = 
                            match value with
                            |ParameterTypes.PInt i -> string i
                            | x -> string x
                        tr [] [
                            td [] [str paramName]
                            td [] [str paramVal]
                            td [] [
                                Button.button 
                                    [ Fulma.Button.OnClick(fun _ -> editParameterBox model (paramName) dispatch)
                                      Fulma.Button.Color IsInfo
                                    ] 
                                    [str "Edit"]
                                Button.button 
                                    [ Fulma.Button.OnClick(fun _ -> deleteParameterBox model (paramName) dispatch )
                                      Fulma.Button.Color IsDanger
                                    ] 
                                    [str "Delete"]
                                ]
                            ]
                        )
                    )
                ]
            Button.button 
                [ Fulma.Button.OnClick(fun _ -> addParameterBox model dispatch)
                  Fulma.Button.Color IsInfo
                ]
                [str "Add Parameter"]
        ]

/// Evaluate parameter expression using parameter bindings - exposed for external use

/// Helper function for simulation: resolve parameter expressions for a component
/// Returns the component type with resolved parameter values
// Create prisms for component type parameter updates using the existing Optics library
let buswidthPrism : Prism<ComponentType, int> =
    Prism.create
        (function
            | Viewer w | Input w | Output w 
            | NbitsAdder w | NbitsAdderNoCin w | NbitsAdderNoCout w | NbitsAdderNoCinCout w
            | NbitsAnd w | NbitsNot w | NbitsOr w | NbitSpreader w | SplitWire w
            | Register w | RegisterE w | Counter w | CounterNoLoad w 
            | CounterNoEnable w | CounterNoEnableLoad w -> Some w
            | BusCompare1 (w, _, _) | Constant1 (w, _, _) | BusSelection (w, _) 
            | NbitsXor (w, _) | Shift (w, _, _) | BusCompare (w, _) 
            | Input1 (w, _) | Constant (w, _) -> Some w
            | _ -> None)
        (fun w compType ->
            match compType with
            | Viewer _ -> Viewer w
            | BusCompare1 (_, cv, dt) -> BusCompare1 (w, cv, dt)
            | BusSelection (_, lsb) -> BusSelection (w, lsb)
            | Constant1 (_, cv, dt) -> Constant1 (w, cv, dt)
            | NbitsAdder _ -> NbitsAdder w
            | NbitsAdderNoCin _ -> NbitsAdderNoCin w
            | NbitsAdderNoCout _ -> NbitsAdderNoCout w
            | NbitsAdderNoCinCout _ -> NbitsAdderNoCinCout w
            | NbitsXor (_, op) -> NbitsXor (w, op)
            | NbitsAnd _ -> NbitsAnd w
            | NbitsNot _ -> NbitsNot w
            | NbitsOr _ -> NbitsOr w
            | NbitSpreader _ -> NbitSpreader w
            | SplitWire _ -> SplitWire w
            | Register _ -> Register w
            | RegisterE _ -> RegisterE w
            | Counter _ -> Counter w
            | CounterNoLoad _ -> CounterNoLoad w
            | CounterNoEnable _ -> CounterNoEnable w
            | CounterNoEnableLoad _ -> CounterNoEnableLoad w
            | Shift (_, sw, st) -> Shift (w, sw, st)
            | BusCompare (_, cv) -> BusCompare (w, cv)
            | Input _ -> Input w
            | Input1 (_, dv) -> Input1 (w, dv)
            | Output _ -> Output w
            | Constant (_, cv) -> Constant (w, cv)
            | _ -> compType)

let ngateInputsPrism : Prism<ComponentType, int> =
    Prism.create
        (function GateN (_, n) -> Some n | _ -> None)
        (fun n -> function GateN (gt, _) -> GateN (gt, n) | t -> t)

let ioPortPrism : Prism<ComponentType, int> =
    Prism.create
        (function Input1 (w, _) | Output w -> Some w | _ -> None)
        (fun w -> function 
            | Input1 (_, dv) -> Input1 (w, dv) 
            | Output _ -> Output w 
            | t -> t)

let resolveParametersForComponent 
    (paramBindings: ParamBindings) 
    (paramSlots: Map<ParamSlot, ConstrainedExpr>) 
    (comp: Component) 
    : Result<Component, string> =
    
    let compIdStr = comp.Id
    let relevantSlots = 
        paramSlots 
        |> Map.filter (fun slot _ -> slot.CompId = compIdStr)

    if Map.isEmpty relevantSlots then
        Ok comp
    else
        relevantSlots
        |> Map.toList
        |> List.fold 
            (fun (currentType, errorOpt) (slot, constrainedExpr) ->
                match errorOpt with
                | Some _ -> (currentType, errorOpt)
                | None ->
                    match ParameterTypes.evaluateParamExpression paramBindings constrainedExpr.Expression with
                    | Ok evaluatedValue -> 
                        let newType =
                            match slot.CompSlot with
                            | Buswidth -> currentType |> (evaluatedValue ^= buswidthPrism)
                            | NGateInputs -> currentType |> (evaluatedValue ^= ngateInputsPrism)
                            | IO _ -> currentType |> (evaluatedValue ^= ioPortPrism)
                            | _ -> currentType
                        (newType, None)
                    | Error err -> (currentType, Some err)
            )
            (comp.Type, None)
        |> function
            | (_, Some err) -> Error err
            | (updatedType, None) -> Ok { comp with Type = updatedType }

/// Update LoadedComponent port labels after parameter resolution
let updateLoadedComponentPorts (loadedComponent: LoadedComponent) : LoadedComponent =
    match loadedComponent.LCParameterSlots with
    | Some paramSlots when not (Map.isEmpty paramSlots.ParamSlots) ->
        // Apply parameter resolution to get updated port labels
        let (comps, conns) = loadedComponent.CanvasState
        let resolvedComps = 
            comps |> List.map (fun comp ->
                match resolveParametersForComponent paramSlots.DefaultBindings paramSlots.ParamSlots comp with
                | Ok resolvedComp -> resolvedComp
                | Error _ -> comp // Keep original on error
            )
        let resolvedCanvas = (resolvedComps, conns)
        let newInputLabels = CanvasExtractor.getOrderedCompLabels (Input1 (0, None)) resolvedCanvas
        let newOutputLabels = CanvasExtractor.getOrderedCompLabels (Output 0) resolvedCanvas
        
        { loadedComponent with 
            InputLabels = newInputLabels
            OutputLabels = newOutputLabels }
    | _ -> loadedComponent

/// Update a custom component with new I/O component widths.
/// Used when these chnage as result of parameter changes.

/// create a popup to edit in the model a custom component parameter binding
/// TODO - maybe comp should be a ComponentId with actual component looked up from model for safety?
let editParameterBindingPopup model parameterName currValue comp (custom: CustomComponentType) dispatch   = 
    match model.CurrentProj with
    | None -> JSHelpers.log "Warning: testEditParameterBox called when no project is currently open"
    | Some project ->
        // Prepare dialog popup.
        let title = "Edit parameter value"
        let compSlotName = CustomCompParam parameterName
        
        // Initialize the popup dialog state to clear any previous parameter specs
        dispatch <| ClearPopupDialogParamSpec compSlotName
        
        let body = fun (model: Model) ->
            div [] [
                str $"New value for the parameter {parameterName}:"
                br []
                str $"(current value: {currValue})"
                br []
                // Use the existing paramInputField with no constraints for custom component parameters
                paramInputField model $"Parameter {parameterName}" currValue (Some currValue) [] (Some comp) compSlotName dispatch
            ]
        
        let buttonText = "Set value"

        // Update the parameter value then close the popup
        let buttonAction =
            fun (model': Model) -> 
                // Get the parameter spec from dialog state
                let paramSpecs = model'.PopupDialogData.DialogState |> Option.defaultValue Map.empty
                match Map.tryFind compSlotName paramSpecs with
                | Some (Ok paramSpec) ->
                    // Parse and evaluate the parameter expression from the spec
                    let paramBindings = model' |> get defaultBindingsOfModel_ |> Option.defaultValue Map.empty
                    match ParameterTypes.evaluateParamExpression paramBindings paramSpec.Expression with
                    | Ok newValue ->
                        let newBindings =
                            match custom.ParameterBindings with
                            | Some bindings -> bindings
                            | None -> Map.empty
                            |> Map.add (ParamName parameterName) (PInt newValue)
                        
                        // Get the custom component's loaded component to find parameter slot definitions
                        let currentSheet = 
                            project.LoadedComponents
                            |> List.tryFind (fun lc -> lc.Name = custom.Name)
                        
                        // Calculate updated label widths based on parameter evaluations
                        let labelToEval = 
                            match currentSheet with
                            | Some sheet ->
                                match sheet.LCParameterSlots with
                                | Some sheetInfo ->
                                    sheetInfo.ParamSlots
                                    |> Map.toSeq
                                    |> Seq.choose (fun (paramSlot, constrainedExpr) -> 
                                        match paramSlot.CompSlot with
                                        | IO label -> 
                                            let evaluatedValue = 
                                                match ParameterTypes.evaluateParamExpression newBindings constrainedExpr.Expression with
                                                | Ok expr -> expr
                                                | Error _ -> 0
                                            Some (label, evaluatedValue)
                                        | _ -> None 
                                    )
                                    |> Map.ofSeq
                                | None -> Map.empty
                            | None -> Map.empty
                        
                        // Update the custom component with new parameter bindings and updated port widths
                        let updatedCustom = updateCustomComponent labelToEval newBindings comp
                        dispatch <| Sheet (SheetT.Wire (BusWireT.Symbol (SymbolT.ChangeCustom (ComponentId comp.Id, comp, updatedCustom.Type))))
                        
                        let dispatchnew (msg: DrawModelType.SheetT.Msg) : unit = dispatch (Sheet msg)
                        model.Sheet.DoBusWidthInference dispatchnew
                        dispatch <| ClosePopup
                    | Error _ -> 
                        // Should not happen as paramInputField already validated the expression
                        ()
                | _ -> 
                    // No valid parameter spec found, don't close popup
                    ()

        // Button is disabled if there's no valid parameter spec
        let isDisabled =
            fun (model': Model) ->
                let paramSpecs = model'.PopupDialogData.DialogState |> Option.defaultValue Map.empty
                match Map.tryFind compSlotName paramSpecs with
                | Some (Ok _) -> false
                | _ -> true

        dialogPopup title body buttonText buttonAction isDisabled [] dispatch

/// UI component for custom component definition of parameter bindings
let makeParamBindingEntryBoxes model (comp:Component) (custom:CustomComponentType) dispatch =
    let ccParams = 
        match custom.ParameterBindings with
        | Some bindings -> bindings
        | None -> Map.empty
    
    let lcDefaultParams =
        match model.CurrentProj with
        | Some proj -> 
            let lcName = List.tryFind (fun c -> custom.Name = c.Name) proj.LoadedComponents
            match lcName with
            | Some lc -> getDefaultParams lc
            | None -> Map.empty
        | None -> Map.empty

    let mergedParamBindings : ParamBindings =
        lcDefaultParams
        |> Map.map (fun key value -> 
            match Map.tryFind key ccParams with
            | Some ccValue -> ccValue // Overwrite if key exists in cc
            | None -> value // use loaded component value if key does not exist in cc
            )
    
    // Get the parameter slots from the current sheet to find expressions
    let slots = model |> getCurrentSheet |> getParamSlots

    match mergedParamBindings.IsEmpty with
    | true ->
        div [] [
            Label.label [] [ str "Parameters" ]
            p [] [str "This component does not use any parameters." ]
        ]   
    | false ->
        div [] [
            Label.label [] [str "Parameters"]
            p [] [str "This component uses the following parameters." ]
            br []
            Table.table [
                        Table.IsBordered
                        Table.IsNarrow
                        Table.IsStriped
                        ] [
                thead [] [
                    tr [] [
                        th [] [str "Parameter"]
                        th [] [str "Value"]
                        th [] [str "Action"]
                    ]
                ]
                tbody [] (
                    mergedParamBindings |> Map.toList |> List.map (fun (key, value) ->
                        let paramName =
                            match key with 
                            | ParameterTypes.ParamName s -> s
                        
                        // Look for the expression in the parameter slots
                        let paramValStr = 
                            let slotKey = {CompId = comp.Id; CompSlot = CustomCompParam paramName}
                            match Map.tryFind slotKey slots with
                            | Some constrainedExpr ->
                                // If there's an expression, render it as a string
                                ParameterTypes.renderParamExpression constrainedExpr.Expression 0
                            | None ->
                                // Otherwise show the evaluated value
                                match value with
                                | ParameterTypes.PInt i -> string i
                                | x -> string x
                        
                        let paramValInt = 
                            match value with
                            | ParameterTypes.PInt i -> i
                            | _ -> 0
                        
                        tr [] [
                            td [] [str paramName]
                            td [] [str paramValStr]
                            td [] [
                                Button.button
                                    [ Fulma.Button.OnClick(fun _ -> editParameterBindingPopup model paramName paramValInt comp custom dispatch)
                                      Fulma.Button.Color IsInfo
                                    ] 
                                    [str "Edit"]
                            ]
                        ]
                    )
                )
            ]
        ]

/// Generate component slots view for design sheet properties panel
/// This is read-only.
let private makeSlotsField (model: ModelType.Model) (comp:LoadedComponent) dispatch = 
    let sheetParamsSlots = getParamsSlot comp

    // Define a function to display PConstraint<int>
    let constraintExpression (constraint': ParamConstraint) =
        match constraint' with
        | MaxVal (expr, err) ->
            div [] [str ("Max: " + ParameterTypes.renderParamExpression expr 0)]
        | MinVal (expr, err) ->
            div [] [str ("Min: " + ParameterTypes.renderParamExpression expr 0)]
    
    let constraintMessage (constraint': ParamConstraint) =
        match constraint' with
            | MaxVal (_, err)  | MinVal (_, err) -> err


    /// UI component to display a single parameterised Component slot definition.
    /// This is read-only.
    let renderSlotSpec (slot: ParamSlot) (expr: ConstrainedExpr) =
        let slotNameStr =
            match slot.CompSlot with
            | Buswidth -> "Buswidth"
            | NGateInputs -> "Num inputs"
            | IO label -> $"Input/output {label}"
            | CustomCompParam paramName -> $"Custom parameter {paramName}"
        
        let name = if Map.containsKey (ComponentId slot.CompId) model.Sheet.Wire.Symbol.Symbols then
                        string model.Sheet.Wire.Symbol.Symbols[ComponentId slot.CompId].Component.Label
                    else
                        "[Nonexistent]" // TODO deleted component slots aren't removed!
        tr [] [
            td [] [
                b [] [str name] 
                br [] 
                str slotNameStr
            ]
            td [] [str (ParameterTypes.renderParamExpression expr.Expression 0)]
            td [
                Class (Tooltip.ClassName + " " + Tooltip.IsTooltipLeft)
                Tooltip.dataTooltip (List.map constraintMessage expr.Constraints |> String.concat "\n")
            ] (List.map constraintExpression expr.Constraints)
        ]

    /// UI component to display parametrised Component slot definitions 
    /// on the properties panel of a design sheet.
    /// This is read-only - changes can be made via the priperties of the component.
    let slotView (slotMap: ComponentSlotExpr) =
        div [Class "component-slots"] [ 
            label [Class "label"] [ str "Parameterised Components"]
            // br []
            p [] [str "This sheet contains the following parameterised components"]
            br []
            Table.table [
                Table.IsBordered
                Table.IsNarrow
                Table.IsStriped
                ] [
                thead [] [
                    tr [] [
                        th [] [str "Component"]
                        th [] [str "Expression"]
                        th [] [str "Constraint"]
                    ]
                ]
                tbody [] (
                        // slots |> Map.toList |> List.map (fun (slot, expr) -> renderSlotSpec slot expr
                        slotMap |> Map.toList |> List.map (fun (slot, expr) -> renderSlotSpec slot expr)
                    )
                ]
        ]

    match sheetParamsSlots with
        |None ->
            div [] [
                Label.label [] [ str "Parameterised Components" ]
                p [] [str "This sheet does not contain any parameterised." ]    
                ]
        |Some sheetParamsSlots -> slotView sheetParamsSlots

/// UI interface for viewing the parameter expressions of a component
let viewParameters (model: ModelType.Model) dispatch =
    
    match model.Sheet.SelectedComponents with
    | [ compId ] ->
        let comp = SymbolUpdate.extractComponent model.Sheet.Wire.Symbol compId
        div [Key comp.Id] [p [] [str $"Currently no parameters added into {comp.Label} sheet." ]    ]    
    | _ -> 
        match model.CurrentProj with
        |Some proj ->
            let sheetName = proj.OpenFileName
            let sheetLdc = proj.LoadedComponents |> List.find (fun ldc -> ldc.Name = sheetName)
            div [] [
            makeParamsField model sheetLdc dispatch
            br []
            makeSlotsField model sheetLdc dispatch]
        |None -> null
