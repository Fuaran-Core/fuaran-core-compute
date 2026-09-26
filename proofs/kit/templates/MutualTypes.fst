(* THE EXTRACTION POST-PASS'S GO-RED FIXTURE — Phase 169.

   This is NOT a template to instantiate and NOT a model of anything. It is the smallest model
   that makes F*'s F# backend emit a mutual TYPE group, and it exists so that
   `../extraction-post-pass.tests.ps1` can prove, on the adopting machine's own pinned prover and
   its own oracle project, all three of:

     - the raw extraction still carries the defect (if it does not, the upstream defect is fixed
       and the post-pass can be retired — the test says so by name);
     - the raw extraction does NOT compile under the oracle project's flags;
     - the extraction the post-pass has run over DOES.

   It is deliberately trivial to verify — one mutual type group, one mutual function over it, no
   lemma and no SMT work worth the name — because it is a fixture for the EXTRACTOR's layout and
   not for the prover. Do not register it in a repository's `$modules`: it earns no committed
   oracle, nothing runs beside it, and the leg would then hold a fixture to a byte diff.

   `node` and `attr` are mutually recursive by construction: a node carries attributes and an
   attribute may carry a node. That is the shape every real mutual model in this directory has,
   and it is the shape the next one will have. *)
module MutualTypes

type node =
  | Leaf of string
  | Branch of attr

and attr =
  | Flag of bool
  | Nested of node

let rec depth_node (n: node) : Tot nat (decreases n) =
  match n with
  | Leaf _ -> 1
  | Branch a -> 1 + depth_attr a

and depth_attr (a: attr) : Tot nat (decreases a) =
  match a with
  | Flag _ -> 1
  | Nested n -> 1 + depth_node n

let depth_positive (n: node) : Lemma (depth_node n >= 1) = ()
