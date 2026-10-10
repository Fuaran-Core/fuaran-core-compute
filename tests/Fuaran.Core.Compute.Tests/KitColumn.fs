namespace Fuaran.Compute.Tests

open Fuaran.Core
open Fuaran.Compute

/// The suite's column builder (Phase 423): a column from cells under a declared type, through the
/// frame's own boundary out. Core `1.0.0` made the column a typed vector, so `Column.create` — total
/// over any cells — left with it. A column is typed where its cells fit the declared type; a cell
/// that does not — the generators' arbitrary cell under a declared type, a date text in a timestamp
/// column — is ABSENT: a `Table` cannot carry it since Core `1.0.0`, so the sample it stood in is
/// drawn without it. A column that disagrees with its schema entry WHOLLY (the suite's mistyped
/// columns, which the evaluator reads boxed) is built under its own type and placed under the
/// other's field by the case that wants it; this builder never infers one, because a sample that
/// held one by accident fed `Union` a column no type holds (a bool column's cells beside an int
/// column's), which no `Table` can carry either.
module KitColumn =

    let private fits (ty: ColumnType) (c: Cell) : bool =
        match c, ty with
        | Null, _ -> true
        | Date s, DateType -> TemporalText.isCanonicalDate s
        | Timestamp s, TimestampType unit -> TemporalText.tryInstant unit s |> Option.isSome
        | _ ->
            match Cell.typeOf c with
            | Some t -> ColumnType.widens t ty
            | None -> true

    let create (name: string) (ty: ColumnType) (cells: Cell list) : Column =
        let field = Field.create name ty

        if cells |> List.forall (fits ty) then
            Vec.columnOfCells field cells
        else
            // The misfits are drawn absent.
            Vec.columnOfCells field (cells |> List.map (fun c -> if fits ty c then c else Null))

/// The compute codecs' refusal of a tag outside a closed vocabulary (Phase 423): Core `1.0.0`'s
/// `UnknownType` names column types only, so a verb, function or mode outside its roster is a
/// `MalformedShape` whose detail names the tag and the admitted spellings — read back here as the
/// pair the suite's roster tests match on.
[<AutoOpen>]
module Refusals =

    let (|UnknownTag|_|) (e: ColumnError) : (string * string list) option =
        match e with
        | MalformedShape detail when detail.StartsWith "unknown tag '" ->
            let afterQuote = detail.Substring("unknown tag '".Length)
            let close = afterQuote.IndexOf "' (expected one of: "

            if close < 0 then
                None
            else
                let got = afterQuote.Substring(0, close)
                let rest = afterQuote.Substring(close + "' (expected one of: ".Length)
                let listText = rest.TrimEnd(')')

                let expected =
                    if listText = "" then
                        []
                    else
                        listText.Split([| ", " |], System.StringSplitOptions.None) |> List.ofArray

                Some(got, expected)
        | _ -> None
