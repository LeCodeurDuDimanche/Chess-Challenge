using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Linq;


// Current problems / improvements to be made : 
// - AI values too much forward pawns and do not want to drop them to defend pieces
//      - This conflicts with protection projection score that take into account that we will defend our pieces
//      - In this case, take into account the higher value of pawns in exchanges (take care to match weights)
// - AI does not keep the king protected
//      - We need a king safety score, especially when a lot of material is in to play
// - AI mass up pieces to get protection score, rather than trying to trap other pieces.
//      - Maybe add points to pinned pieces, try to get our pieces focusing the enmey king
// - AI is prone to draw by repetition in case of local maximums
// - AI is buggy when playing black (leaves piece hanging in one)
// - Although overall play seems more solid, due to the blunders done by the AI and its incapability to make a plan late game, with loose to AI2

// Remarks:
// - AI does not value specially ennemy advanced pawns
namespace BotAdrien3WithProtectors{
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

    public class BotAdrien3WithProtectors : IChessBot
    {
        private bool weAreWhite;            
        readonly float[] PIECE_POINTS = {0, 1, 3, 3, 5, 9, 0};

        private Dictionary<string, MoveAndScoreAndDepth> scores;

        private const float BLUNDER_MOVE_SCORE = -5, TOO_GOOD_TO_WORK_MOVE_SCORE = 10; 

        public BotAdrien3WithProtectors() {
            scores = new Dictionary<string, MoveAndScoreAndDepth>();
        }

        public List<Piece> getPiecesThatCanMoveOn(Board board, Square dest, bool piecesAreWhite) {
            List<Piece> result = new List<Piece>();
            int index = piecesAreWhite ? 0 : 6;
            PieceList[] pieceLists = board.GetAllPieceLists().ToList().GetRange(index, 6).ToArray();
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

        public List<PieceType> getProtectorsOrAttackers(Board board, Piece piece, bool isProtector) {
            return getPiecesThatCanMoveOn(board, piece.Square, isProtector ? piece.IsWhite : !piece.IsWhite).Select(piece => piece.PieceType).ToList();
        }

        private float ResolveFight(PieceType piece, List<PieceType> protectors, List<PieceType> attackers) {
            int nbProtectors = protectors.Count(), nbAttackers = attackers.Count();
            protectors.Insert(0, piece);
            protectors.Sort();
            attackers.Sort();

            IEnumerable<float> protectorScores = protectors.Select(pt => PIECE_POINTS[(int) pt]), attackerScores = attackers.Select(pt => PIECE_POINTS[(int) pt]);

            float fightScore = 0, remainingProtectionScore = 0;

            if (nbProtectors >= nbAttackers)
            {
                fightScore = attackerScores.Sum() - protectorScores.Take(nbAttackers).Sum();
                remainingProtectionScore = (nbProtectors - nbAttackers) * protectorScores.ElementAt(nbAttackers) * .3f;
            } else {
                fightScore = attackerScores.Take(nbProtectors).Sum() - protectorScores.Sum();
            }
            // Cap the fightScore to 0: an attacker that will loose a fight will not provoke it
            return Math.Min(0, fightScore) + remainingProtectionScore;

        }

        public float Evaluate(Board board, bool log = false) {
            if (board.IsInCheckmate())
                return (board.IsWhiteToMove == weAreWhite ? -1 : 1) * 50000;
            
            PieceList[] pieceList = board.GetAllPieceLists();

            float piecesScore = 0;
            int colourMod = weAreWhite ? 1 : -1;
            for (int i = 0; i < 12; i++) {
                foreach (Piece piece in pieceList[i]){
                    piecesScore +=  PIECE_POINTS[(int) piece.PieceType] * (i < 6 ? colourMod: -colourMod);
                }
                
            }

            float protectionScore = 0;
            for (int i = 0; i < 12; i++) {
                // Skip the king, this is already accoutned in check points
                if (i % 6 == (int) PieceType.King - 1)
                    continue;

                foreach (Piece piece in pieceList[i]){
                    List<float> attackersValue = new List<float>(), protectorsValue = new List<float>();

                    int ennemyScoreInverter = (i < 6 ? colourMod: -colourMod);
                    float fightScore = ResolveFight(piece.PieceType, getProtectorsOrAttackers(board, piece, true), getProtectorsOrAttackers(board, piece, false));
                    
                    // Console.WriteLine("Piece " + piece + " at " + piece.Square.Name + " is protected " +getProtectorsOrAttackers(board, piece, true).Count() + " times and attacked " + getProtectorsOrAttackers(board, piece, false).Count() + " times, score is "+ fightScore);
                    protectionScore += fightScore * ennemyScoreInverter;
                }

            }


            float posScore = 0;
            foreach (Piece piece in board.GetPieceList(PieceType.Pawn, weAreWhite))
            {
                posScore += (4.5f - Math.Abs(3.5f - piece.Square.File)) * (weAreWhite ? piece.Square.Rank : 7 - piece.Square.Rank);
            }
            
            float totalScore = (16 * piecesScore + 2 * protectionScore + 2 * posScore) / 20;
            // if (log) Console.WriteLine("Tot score: " + totalScore + ", pieces scores: " + piecesScore + ", posScore: " + posScore + ", protectionScore: " + protectionScore);
            return totalScore;

        }

        public float EvaluateMove(Board board, Move move, bool log = false) {
            board.MakeMove(move);
            float score = Evaluate(board, log);
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
               // .Where(move => ourTurn || (move.score - currentScore <= -BLUNDER_MOVE_SCORE))
                // .Where(move => !ourTurn || (move.score - currentScore <= TOO_GOOD_TO_WORK_MOVE_SCORE))
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
                    //Console.WriteLine("For move " + move.move + " : " + EvaluateMove(board, move.move, true));
                    bestLocalMove.score = move.score;
                }
                else {
                    board.MakeMove(move.move);
                    //Console.WriteLine(spaces + "Trying " + move.move + " with score " +  move.score +  " at depth " + maxDepth + "...");
                    bestLocalMove = Search(board, maxDepth - 1, !ourTurn);
                   // Console.WriteLine(spaces + "Undoing " + move.move  + " with real score " +  bestLocalMove.score + " at depth " + maxDepth + "...\n");
                    board.UndoMove(move.move);           
                }
                if ((bestLocalMove.score > bestMove.score && ourTurn) || (bestLocalMove.score < bestMove.score && !ourTurn))
                {
                   // Console.WriteLine(spaces + "Move " + move.move + " is better than " + bestMove.move + " for " + (ourTurn  ? "us": "them") + " at depth " + maxDepth + " with score " + bestLocalMove.score);
                    bestMove.score = bestLocalMove.score;
                    bestMove.move = move.move;
                }
            }
            scores[key] = new MoveAndScoreAndDepth(bestMove.move, bestMove.score, maxDepth);

            //Console.WriteLine(spaces + "Returning " + bestMove.move + " on depth " + maxDepth + " with score " + bestMove.score + ", got " + moves.Length +" moves");
            return bestMove;
        }

        public Move Think(Board board, Timer timer)
        {
            weAreWhite = board.IsWhiteToMove;
            MoveAndScore bestMove = Search(board, timer.MillisecondsRemaining > 30000 ? 2 : timer.MillisecondsRemaining / 10000);
            //Console.WriteLine("Got move " + bestMove.move + " with score " + bestMove.score /*+ "\n\n\n           ============"*/);

            return bestMove.move;
        }
    }
}