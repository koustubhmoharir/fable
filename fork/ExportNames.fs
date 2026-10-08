namespace Fable.Cli.Fork

open System.Collections.Generic
open Fable.AST.Babel

module ExportNames =
    let private declarationNames =
        function
        | ClassDeclaration(_, Some(Identifier(name, _)), _, _, _, _, _, _)
        | FunctionDeclaration(_, _, Identifier(name, _), _, _, _, _)
        | InterfaceDeclaration(Identifier(name, _), _, _, _, _)
        | EnumDeclaration(name, _, _)
        | TypeAliasDeclaration(name, _, _) -> [ name ]
        | ClassDeclaration(_, None, _, _, _, _, _, _) -> []
        | Declaration.VariableDeclaration(VariableDeclaration(declarations, _, _)) ->
            declarations
            |> Array.map (fun (VariableDeclarator(name = name)) -> name)
            |> Array.toList

    let fromProgram (Program body) =
        let names = HashSet<string>()

        for declaration in body do
            match declaration with
            | ExportNamedDeclaration declaration ->
                for name in declarationNames declaration do
                    names.Add(name) |> ignore
            | ExportDefaultDeclaration _ -> names.Add("default") |> ignore
            | ExportNamedReferences(specifiers, _) ->
                for ExportSpecifier(_, Identifier(name, _)) in specifiers do
                    names.Add(name) |> ignore
            | _ -> ()

        Array.ofSeq names
