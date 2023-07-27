using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Linq;

public struct MoveAndScore {
    public Move move;
    public float score;
}

public class BotAdrien : IChessBot
{
    private bool weAreWhite;
    private Dictionary<string, MoveAndScore> scores;

    public BotAdrien() {
        scores = new Dictionary<string, MoveAndScore>();
    }

    public List<Piece> getPiecesThatCanMoveOn(Board board, Square dest, bool piecesAreWhite) {
        List<Piece> result = new List<Piece>();
        int index = piecesAreWhite ? 0 : 6;
        PieceList[] pieceLists = board.GetAllPieceLists().ToArray();
        foreach (PieceList pieceList in pieceLists)
        {
            foreach(Piece piece in pieceList) {
                ulong possibleMoves = piece.PieceType switch {
                    PieceType.Knight => BitboardHelper.GetKnightAttacks(piece.Square),
                    PieceType.King => BitboardHelper.GetKingAttacks(piece.Square),
                    PieceType.Pawn => BitboardHelper.GetPawnAttacks(piece.Square, piece.IsWhite),
                    _ => BitboardHelper.GetSliderAttacks(piece.PieceType, piece.Square, board)
                };
                if (BitboardHelper.SquareIsSet(possibleMoves, dest))
                    result.Add(piece);

            }
        }
       return result;
    }

    public PieceType[] getProtectorsorAttackers(Board board, Piece piece, bool isProtector) {
        return getPiecesThatCanMoveOn(board, piece.Square, isProtector ? piece.IsWhite : !piece.IsWhite).Select(piece => piece.PieceType).ToArray();

    }

    public float Evaluate(Board board, bool log = false) {
        float[] points = {0, 1, 3, 3, 5, 9, 0};
        float[] pointsByProtector = {0, 3, 2, 2, 2, 1.5f, 1.2f};

        if (board.IsInCheckmate())
            return (board.IsWhiteToMove == weAreWhite ? -1 : 1) * 50000;
        
        PieceList[] pieceList = board.GetAllPieceLists();

        float piecesScore = 0;
        int colourMod = weAreWhite ? 1 : -1;
        for (int i = 0; i < 12; i++) {
            foreach (Piece piece in pieceList[i]){
                piecesScore +=  points[(int) piece.PieceType] * (i < 6 ? colourMod: -colourMod);
            }
            
        }

        float protectionScore = 0;
        for (int i = 0; i < 12; i++) {
            foreach (Piece piece in pieceList[i]){
                foreach(PieceType protectorType in getProtectorsorAttackers(board, piece, true)) {
                     protectionScore += pointsByProtector[(int) protectorType] * (i < 6 ? colourMod: -colourMod);
                }
                foreach(PieceType attackerType in getProtectorsorAttackers(board, piece, false)) {
                    float pointDelta = points[(int) piece.PieceType] - points[(int) attackerType];
                    protectionScore -= Math.Max(0, pointDelta) * (i < 6 ? colourMod: -colourMod);
                }
            }
            
        }


        float posScore = 0;
        foreach (Piece piece in board.GetPieceList(PieceType.Pawn, weAreWhite))
        {
            posScore += weAreWhite ? piece.Square.Rank : 7 - piece.Square.Rank;
        }
        
        float totalScore = (8 * piecesScore + 1 * protectionScore + 2 * posScore) / 11;
        if (log) Console.WriteLine("Tot score: " + totalScore + ", pieces scores: " + piecesScore + ", posScore: " + posScore + ", protectionScore: " + protectionScore);
        return totalScore;

    }

    public float EvaluateMove(Board board, Move move) {
        board.MakeMove(move);
        float score = Evaluate(board);
        board.UndoMove(move);
        return score;
    }

    public Move[] GetValuableMoves(Board board, bool ourTurn) {
        // float bestScore = ourTurn ? float.MinValue : float.MaxValue;
        // List<Move> bestMoves = new List<Move>();
        // foreach (Move move in board.GetLegalMoves()) {
        //     board.MakeMove(move);
        //     float score = Evaluate(board);
        //     if ((score > bestScore && ourTurn) || (score < bestScore && !ourTurn)) {
        //         bestScore = score;
        //         bestMoves.Clear();
        //     }
        //     if (score == bestScore)
        //         bestMoves.Add(move);

        //     board.UndoMove(move);
        // }

        List<Move> bestMoves = new List<Move>(board.GetLegalMoves());

        bestMoves.Sort((move1, move2) => (ourTurn ? 1  : -1) *(int)(EvaluateMove(board, move2) - EvaluateMove(board, move1)));

       // Console.WriteLine("Returning " + bestMoves.Count + " best moves");
        
        //TODO: as read only
        return bestMoves.GetRange(0, Math.Min(bestMoves.Count, 6)).ToArray();
    }


    public MoveAndScore Search(Board board, int maxDepth, bool ourTurn = true) {        
       // Console.WriteLine("Depth "+ maxDepth);
        string key = board.GetFenString();
        if (scores.ContainsKey(key)) {
            return scores[key];
        }

        Move[] moves = GetValuableMoves(board, ourTurn); // GetValuableMoves(board, ourTurn);
        MoveAndScore bestMove = new MoveAndScore();
        bestMove.score = ourTurn ? float.MinValue : float.MaxValue;
        foreach (Move move in moves) {
            board.MakeMove(move);
            MoveAndScore bestLocalMove;
            if (maxDepth == 0) {
                // Console.Write("For move " + move + " : ");
                bestLocalMove.score = Evaluate(board, false);
            }
            else{
                // Console.WriteLine("Trying " + move + "at depth " + maxDepth + "...");
                bestLocalMove = Search(board, maxDepth - 1, !ourTurn);
               //  Console.WriteLine("Undoing " + move + "at depth " + maxDepth + "...\n");
            }
            if ((bestLocalMove.score > bestMove.score && ourTurn) || (bestLocalMove.score < bestMove.score && !ourTurn))
            {
               // Console.WriteLine("Move " + move + " is better than " + bestMove.move + " for " + (ourTurn  ? "us": "them") + " at depth " + maxDepth + " with score 0 " + bestLocalMove.score);
                bestMove.score = bestLocalMove.score;
                bestMove.move = move;
            }
            board.UndoMove(move);            

        }
        scores[key] = bestMove;

        // Console.WriteLine("Returning " + bestMove.move + " on depth " + maxDepth + " with score " + bestMove.score + ", got " + moves.Length +" moves");
        return bestMove;
    }

    public Move Think(Board board, Timer timer)
    {

        MoveAndScore bestMove = Search(board, timer.MillisecondsRemaining > 30000 ? 3 : timer.MillisecondsRemaining / 10000);
        return bestMove.move;
    }
}