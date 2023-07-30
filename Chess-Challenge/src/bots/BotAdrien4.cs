using ChessChallenge.API;
using System.Collections.Generic;
using System;
using System.Linq;


// Current problems / improvements to be made : 
// - AI mass up pieces to get protection score, rather than trying to trap other pieces.
//      - Maybe add points to pinned pieces, try to get our pieces focusing the ennemy king
// - AI does not put out the rook if it has not castled
// - AI is prone to draw by repetition in case of local maximums
//      - Give negative weight to (last) repeated move if we're winning, positive (proportional for first, then second, etc.) if we're loosing
// - AI drops pieces if a full trade is equal or good, but does not consider part of the trade only
// - AI is buggy when playing white (leaves pieces hanging in one)
//      => To investigate

// TODO:
// Implement alpha-beta and parametric depth search
// Implement time-based rather than depth-based limitations (will greatly improve late game)
// Implement late-game strategy

// Leaves left and right paxns unprotected
// Remarks:
// - AI does not value specially ennemy advanced pawns
namespace BotAdrien4{
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

    public class BotAdrien4 : IChessBot
    {
        private bool weAreWhite;            
        readonly float[] PIECE_POINTS = {0, 1, 3, 3, 5, 9, 0};

        private Dictionary<string, MoveAndScoreAndDepth> scores;

        private const float BLUNDER_MOVE_SCORE = -5, TOO_GOOD_TO_WORK_MOVE_SCORE = 10, CHECKMATE_SCORE = 10000;
        private const float PROTECT_NON_THREATENED_SCORE_WEIGHT=.05f;
        private const float ATTACK_PIECES_BUT_NOT_ON_OUR_TURN_SCORE_WEIGHT=.025f;

        public BotAdrien4() {
            scores = new Dictionary<string, MoveAndScoreAndDepth>();
        }

        public IEnumerable<Piece> getPiecesThatCanMoveOn(List<Piece>[] piecesMoves, Square dest, bool piecesAreWhite) {
            return new List<Piece>(piecesMoves[dest.Index]).Where(piece => piece.IsWhite == piecesAreWhite);
        }

        public List<PieceType> getProtectorsOrAttackers(List<Piece>[] piecesMoves, Piece piece, bool isProtector) {
            return getPiecesThatCanMoveOn(piecesMoves, piece.Square, isProtector ? piece.IsWhite : !piece.IsWhite).Select(piece => piece.PieceType).ToList();
        }

        private float ResolveFight(PieceType piece, List<PieceType> protectors, List<PieceType> attackers, bool isPieceTeamTurn, bool log = false) {
            int nbProtectors = protectors.Count(), nbAttackers = attackers.Count();
            protectors.Sort();
            protectors.Insert(0, piece);
            attackers.Sort();

            //FIXME: bug, the oponent will never play a not profitable attack BUT will conduct profitable parts of the attack
            // - Find a generic way of resolving attacks until it does not profit to the attacker anymore
            // - We can keep the Math.Min cap if the algo does not guarantee it

            IEnumerable<float> protectorScores = protectors.Select(pt => PIECE_POINTS[(int) pt]), attackerScores = attackers.Select(pt => PIECE_POINTS[(int) pt]);

            float fightScore = 0, remainingProtectionScore = 0;

            if (nbProtectors >= nbAttackers)
            {
                fightScore = attackerScores.Sum() - protectorScores.Take(nbAttackers).Sum();
                remainingProtectionScore = (float)Math.Sqrt(nbProtectors - nbAttackers) * protectorScores.ElementAt(nbAttackers) * PROTECT_NON_THREATENED_SCORE_WEIGHT;
            } else {
                fightScore = attackerScores.Take(nbProtectors).Sum() - protectorScores.Sum();
            }
            if (isPieceTeamTurn) fightScore *= ATTACK_PIECES_BUT_NOT_ON_OUR_TURN_SCORE_WEIGHT;

            if (log) Console.WriteLine("\t It is " + (isPieceTeamTurn ? "this" : "other")+ " piece team to play, fightScore is " + fightScore + " protection score is " + remainingProtectionScore);
            // Cap the fightScore to 0: an attacker that will loose a fight will not provoke it
            return Math.Min(0, fightScore) + remainingProtectionScore;

        }

        public float GetKingPlacementScore(Board board, bool weAreWhite) {
            Square pos = board.GetKingSquare(weAreWhite);
            float rankDistance = weAreWhite ? pos.Rank : 7- pos.Rank;
            float centerFileDistance = Math.Abs(3.5f - pos.File) - 0.5f;
            return - rankDistance + (centerFileDistance > 1 ? 4 : 0);
        }

        public float Evaluate(Board board, bool log = false) {
            if (board.IsInCheckmate())
                return (board.IsWhiteToMove == weAreWhite ? -1 : 1) * CHECKMATE_SCORE;
            
            PieceList[] pieceLists = board.GetAllPieceLists();

            float piecesScore = 0, totalMaterialOnBoard = 0;
            int colourMod = weAreWhite ? 1 : -1;
            for (int i = 0; i < 12; i++) {
                foreach (Piece piece in pieceLists[i]){
                    piecesScore +=  PIECE_POINTS[(int) piece.PieceType] * (i < 6 ? colourMod: -colourMod);
                    totalMaterialOnBoard += PIECE_POINTS[(int) piece.PieceType];
                }
                
            }

            // Get pieces moves
            List<Piece>[] piecesMoves = new List<Piece>[64];
            for (int i = 0; i < 64; i++)
                piecesMoves[i] = new List<Piece>();

            foreach (PieceList pieceList in pieceLists)
            {
                foreach(Piece piece in pieceList) {
                    ulong possibleMoves = piece.PieceType switch {
                        PieceType.Knight => BitboardHelper.GetKnightAttacks(piece.Square),
                        PieceType.King => BitboardHelper.GetKingAttacks(piece.Square),
                        PieceType.Pawn => BitboardHelper.GetPawnAttacks(piece.Square, piece.IsWhite),
                        _ => BitboardHelper.GetSliderAttacks(piece.PieceType, piece.Square, board)
                    };

                    for (int i = 0; i < 64; i++) {
                        if (BitboardHelper.SquareIsSet(possibleMoves, new Square(i)))
                            piecesMoves[i].Add(piece);
                    }

                }
            }


            // This loop can be improved, this is n^2 * m (n number pieces, m mean number of moves by piece) but can be improved to be n * n  + n 
            // prefetching each piece moves
            float protectionScore = 0;
            for (int i = 0; i < 12; i++) {
                // Skip the king, this is already accoutned in check points
                if (i % 6 == (int) PieceType.King - 1)
                    continue;

                foreach (Piece piece in pieceLists[i]){
                    List<float> attackersValue = new List<float>(), protectorsValue = new List<float>();

                    int ennemyScoreInverter = (i < 6 ? colourMod: -colourMod);
                    float fightScore = ResolveFight(piece.PieceType, getProtectorsOrAttackers(piecesMoves, piece, true), getProtectorsOrAttackers(piecesMoves, piece, false), board.IsWhiteToMove == i < 6, log);
                    
                    if (log) Console.WriteLine("Piece " + piece + " at " + piece.Square.Name + " is protected " +getProtectorsOrAttackers(piecesMoves, piece, true).Count() + " times and attacked " + getProtectorsOrAttackers(piecesMoves, piece, false).Count() + " times, score is "+ fightScore);
                    protectionScore += fightScore * ennemyScoreInverter;
                }
            }

            float kingSafetyScore = 0;
            if (totalMaterialOnBoard >= 28)
            {
                kingSafetyScore += GetKingPlacementScore(board, weAreWhite);
                kingSafetyScore -= GetKingPlacementScore(board, !weAreWhite);
            }


            float posScore = 0;
            foreach (Piece piece in board.GetPieceList(PieceType.Pawn, weAreWhite))
            {
                float fileWeight;
                if (Math.Abs(3.5f - board.GetKingSquare(weAreWhite).File) >= 2.5f)
                    // This gives a 2 point increment for each rank advanced for pawns with fiele distance of 2 and more
                    // -2 for others (protecting the king)
                    fileWeight = Math.Abs(board.GetKingSquare(weAreWhite).File - piece.Square.File) >= 2 ? 2 : -2;
                else 
                    // This gives a 1.5 point increment for each rank advanced for central pawns, 
                    // 0.5 point per rank for c and f, -0.5 point per rank for b and g and -1.5 for a and h
                    fileWeight= (2f - Math.Abs(3.5f - piece.Square.File));


                posScore += fileWeight * (weAreWhite ? piece.Square.Rank - 1 : 6 - piece.Square.Rank);
            }
            
            float totalScore = (5 * piecesScore + 3 * protectionScore + 2 * kingSafetyScore + 1 * posScore) / 11;
            if (log) Console.WriteLine("Tot score: " + totalScore + ", pieces scores: " + piecesScore + ", posScore: " + posScore + ", protectionScore: " + protectionScore);
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
                .Where(move => ourTurn || (move.score - currentScore <= -BLUNDER_MOVE_SCORE))
               // This is blunderistic :  .Where(move => !ourTurn || (move.score - currentScore <= -BLUNDER_MOVE_SCORE))
                .ToArray();
        }


        public MoveAndScore Search(Board board, int maxDepth, bool ourTurn = true) {        
            string key = board.GetFenString();
            if (scores.ContainsKey(key) && scores[key].depth >= maxDepth) {
                return new MoveAndScore(scores[key].move, scores[key].score);
            }

            string spaces =new string(' ', 3 * (3 - maxDepth));

            float baseScore = Evaluate(board); 

            MoveAndScore[] moves = GetValuableMoves(board, baseScore, ourTurn);
            if (moves.Length == 0)
                return new MoveAndScore(Move.NullMove, baseScore);

            MoveAndScore bestMove = moves[0];
            bestMove.score = ourTurn ? float.MinValue : float.MaxValue;
            foreach (MoveAndScore move in moves) {
                float scoreDelta = move.score - baseScore;
                MoveAndScore bestLocalMove;
                    
                if (maxDepth <= 1) {
                    //Console.WriteLine("** For move " + move.move);
                    //Console.WriteLine(EvaluateMove(board, move.move, true));
                    bestLocalMove.score = move.score;
                }
                else {
                    board.MakeMove(move.move);
                    //Console.WriteLine(spaces + "Trying " + move.move + " with score " +  move.score +  " at depth " + maxDepth + "...");
                    bestLocalMove = Search(board, maxDepth - 1, !ourTurn);
                    //Console.WriteLine(spaces + "Undoing " + move.move  + " with real score " +  bestLocalMove.score + " at depth " + maxDepth + "...\n");
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
            MoveAndScore bestMove = Search(board, timer.MillisecondsRemaining > 30000 ? 3 : 1 + timer.MillisecondsRemaining / 10000);
            Console.WriteLine("Got move " + bestMove.move + " with score " + bestMove.score + "\n\n\n           ============");

            return bestMove.move;
        }
    }
}