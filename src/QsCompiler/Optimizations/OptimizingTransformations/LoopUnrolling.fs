// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Quantum.QsCompiler.Experimental

open Microsoft.Quantum.QsCompiler.Experimental.Utils
open Microsoft.Quantum.QsCompiler.SyntaxExtensions
open Microsoft.Quantum.QsCompiler.SyntaxTokens
open Microsoft.Quantum.QsCompiler.SyntaxTree
open Microsoft.Quantum.QsCompiler.Transformations


/// The SyntaxTreeTransformation used to unroll loops
type LoopUnrolling private (_private_ : string) =
    inherit TransformationBase()

    new (callables, maxSize) as this = 
        new LoopUnrolling("_private_") then
            this.Namespaces <- new LoopUnrollingNamespaces(this)
            this.StatementKinds <- new LoopUnrollingStatementKinds(this, callables, maxSize)

/// private helper class for LoopUnrolling
and private LoopUnrollingNamespaces (parent : LoopUnrolling) = 
    inherit NamespaceTransformationBase(parent)

    override __.onNamespace x =
        let x = base.onNamespace x
        VariableRenaming().Namespaces.onNamespace x

/// private helper class for LoopUnrolling
and private LoopUnrollingStatementKinds (parent : LoopUnrolling, callables, maxSize) = 
    inherit Core.StatementKindTransformation(parent)

    override this.onForStatement stm =
        let loopVar = fst stm.LoopItem |> this.onSymbolTuple
        let iterVals = this.Expressions.Transform stm.IterationValues
        let loopVarType = this.Expressions.Types.Transform (snd stm.LoopItem)
        let body = this.Statements.onScope stm.Body
        maybe {
            let! iterValsList =
                match iterVals.Expression with
                | RangeLiteral _ when isLiteral callables iterVals ->
                    rangeLiteralToSeq iterVals.Expression |> Seq.map (IntLiteral >> wrapExpr Int) |> List.ofSeq |> Some
                | ValueArray va -> va |> List.ofSeq |> Some
                | _ -> None
            do! check (iterValsList.Length <= maxSize)
            let iterRange = iterValsList |> List.map (fun x ->
                let variableDecl = QsBinding.New ImmutableBinding (loopVar, x) |> QsVariableDeclaration |> wrapStmt
                let innerScope = { stm.Body with Statements = stm.Body.Statements.Insert(0, variableDecl) }
                innerScope |> newScopeStatement |> wrapStmt)
            let outerScope = QsScope.New (iterRange, stm.Body.KnownSymbols)
            return outerScope |> newScopeStatement |> this.Transform
        }
        |? (QsForStatement.New ((loopVar, loopVarType), iterVals, body) |> QsForStatement)

