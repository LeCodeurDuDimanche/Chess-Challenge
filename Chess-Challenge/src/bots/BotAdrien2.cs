using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Linq;

namespace BotAdrien2{
    public struct MoveAndScore {
        public Move move;
        public float score;

        public MoveAndScore(Move m, float s) {
            move = m;
            score = s;
        }
    }

    public struct MoveAndScoreAndDepth {
        public Move move;
        public float score;
        public int depth;

        public MoveAndScoreAndDepth(Move m, float s, int d) {
            move = m;
            score = s;
            depth = d;
        }
    }

    public class BotAdrien2 : IChessBot
    {
        private bool weAreWhite;
        private Dictionary<string, MoveAndScoreAndDepth> scores;

        private const float BLUNDER_MOVE_SCORE = -5, TOO_GOOD_TO_WORK_MOVE_SCORE = 10; 

        public BotAdrien2() {
            scores = new Dictionary<string, MoveAndScoreAndDepth>();
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
                    float pieceProtectorsValue = 0, nbTimesProtected = 0;
                    foreach(PieceType protectorType in getProtectorsorAttackers(board, piece, true)) {
                        pieceProtectorsValue += points[(int) protectorType];
                        nbTimesProtected++;
                    }
                    foreach(PieceType attackerType in getProtectorsorAttackers(board, piece, false)) {
                        pieceProtectorsValue -= points[(int) attackerType];
                        nbTimesProtected--;
                    }
                    if (nbTimesProtected == 0) {
                        protectionScore -= pieceProtectorsValue * 2 * (i < 6 ? colourMod: -colourMod);
                    }
                    else {
                        protectionScore += nbTimesProtected * points[(int) piece.PieceType] * (i < 6 ? colourMod: -colourMod);
                    }
                }
                
            }


            float posScore = 0;
            foreach (Piece piece in board.GetPieceList(PieceType.Pawn, weAreWhite))
            {
                posScore += weAreWhite ? piece.Square.Rank : 7 - piece.Square.Rank;
            }
            
            float totalScore = (10 * piecesScore + 2 * protectionScore + 1 * posScore) / 13;
            //Console.WriteLine("Tot score: " + totalScore + ", pieces scores: " + piecesScore + ", posScore: " + posScore + ", protectionScore: " + protectionScore);
            return totalScore;

        }

        public float EvaluateMove(Board board, Move move) {
            board.MakeMove(move);
            float score = Evaluate(board);
            board.UndoMove(move);
            return score;
        }

        public MoveAndScore[] GetValuableMoves(Board board, float currentScore, bool ourTurn) {

            List<Move> bestMoves = new List<Move>(board.GetLegalMoves());

            List<MoveAndScore> bestMoveWithScore = bestMoves.Select(move => new MoveAndScore(move, EvaluateMove(board, move))).ToList();
            //bestMoveWithScore.Sort((move1, move2) => (ourTurn ? 1  : -1) *(int)(move2.score - move1.score));
            // Console.WriteLine("Returning " + bestMoves.Count + " best moves");
            
            //TODO: as read only
            return bestMoveWithScore
                .Where(move => !ourTurn || (move.score - currentScore >= BLUNDER_MOVE_SCORE))
                .ToArray();
        }


        public MoveAndScore Search(Board board, int maxDepth, bool ourTurn = true) {        
        // Console.WriteLine("Depth "+ maxDepth);
            string key = board.GetFenString();
            if (scores.ContainsKey(key) && scores[key].depth >= maxDepth) {
                return new MoveAndScore(scores[key].move, scores[key].score);
            }

            string spaces =new string(' ', 2 * (2 - maxDepth));

            float baseScore = Evaluate(board); 

            MoveAndScore[] moves = GetValuableMoves(board, baseScore, ourTurn); // GetValuableMoves(board, ourTurn);
            if (moves.Length == 0)
                return new MoveAndScore(Move.NullMove, baseScore);

            MoveAndScore bestMove = moves[0];
            bestMove.score = ourTurn ? float.MinValue : float.MaxValue;
            foreach (MoveAndScore move in moves) {
                float scoreDelta = move.score - baseScore;
                // If this move is a blunder (the score suddendly dropped), ignore it and do not go further
                // if (scoreDelta < BLUNDER_MOVE_SCORE)
                //     continue;
                // else if (scoreDelta > TOO_GOOD_TO_WORK_MOVE_SCORE) // If this move is an evident blunder by the opponent, ignore it and do not go further
                //     continue;

                MoveAndScore bestLocalMove;
                    
                if (maxDepth == 0) {
                    // Console.Write("For move " + move + " : ");
                    bestLocalMove.score = move.score;
                }
                else {
                    board.MakeMove(move.move);
                    Console.WriteLine(spaces + "Trying " + move.move + " with score " +  move.score +  " at depth " + maxDepth + "...");
                    bestLocalMove = Search(board, maxDepth - 1, !ourTurn);
                    Console.WriteLine(spaces + "Undoing " + move.move  + " with real score " +  bestLocalMove.score + " at depth " + maxDepth + "...\n");
                    board.UndoMove(move.move);           
                }
                if ((bestLocalMove.score > bestMove.score && ourTurn) || (bestLocalMove.score < bestMove.score && !ourTurn))
                {
                    Console.WriteLine(spaces + "Move " + move.move + " is better than " + bestMove.move + " for " + (ourTurn  ? "us": "them") + " at depth " + maxDepth + " with score " + bestLocalMove.score);
                    bestMove.score = bestLocalMove.score;
                    bestMove.move = move.move;
                }
            }
            scores[key] = new MoveAndScoreAndDepth(bestMove.move, bestMove.score, maxDepth);

            Console.WriteLine(spaces + "Returning " + bestMove.move + " on depth " + maxDepth + " with score " + bestMove.score + ", got " + moves.Length +" moves");
            return bestMove;
        }

        public Move Think(Board board, Timer timer)
        {
            weAreWhite = board.IsWhiteToMove;
            MoveAndScore bestMove = Search(board, timer.MillisecondsRemaining > 30000 ? 2 : timer.MillisecondsRemaining / 10000);
            Console.WriteLine("Got move " + bestMove.move + " with score " + bestMove.score + "\n\n\n           ============");

            return bestMove.move;
        }
    }
}