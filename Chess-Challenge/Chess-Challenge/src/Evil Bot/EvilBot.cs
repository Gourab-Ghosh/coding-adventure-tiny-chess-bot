using ChessChallenge.API;
using System;
using static System.Math;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
    {
        Move bestMove;
    static readonly  ulong transpositionTableSize = 8388608; // 128 MB #DEBUG
    // I generated an issue that we should have a separate bar for the transposition table size but it was not officially supported.
    // So for this reason I had to set the transposition table size to 128 MB to be on safe size which takes 8388608 cells.
    readonly (ulong, byte, short, byte, ushort)[] transpositionTable = new (ulong, byte, short, byte, ushort)[transpositionTableSize]; // (hash, depth, score, flag, bestMoveRawValue)

    public Move Think(Board board, Timer timer)
    {
        // int timePerMove = Max(10, timer.MillisecondsRemaining / 30 + timer.IncrementMilliseconds - 100)
        int timePerMove = Min(500 * board.PlyCount, Max(
            10,
            (timer.MillisecondsRemaining - Max(0, timer.OpponentMillisecondsRemaining - timer.MillisecondsRemaining)) / 15 + timer.IncrementMilliseconds - 100
        ))
        , ply = 0
        , depth = 1;
        if (timer.OpponentMillisecondsRemaining <= 60_000) // #Debug
            timePerMove = 100; // #DEBUG
        if (timer.OpponentMillisecondsRemaining > 1000000) // #Debug
            timePerMove = Max(10, timer.MillisecondsRemaining / 30 + timer.IncrementMilliseconds - 100); // #DEBUG
        int numKillerMoves = 3; // #DEBUG
        ulong nodes = 0; // #DEBUG
        var killerMoves = new Move[150, numKillerMoves]; // 150 is the maximum ply;
        int seldepth; // #DEBUG
        int[] pieceValuesAndPositionIndices = {
            0, 100, 320, 330, 500, 900, 0, // Piece values (King is the most useless piece in chess so it's value is 0 :p)
            -50, -40, -30, -20, -15, -10, -5, 0, 5, 10, 15, 20, 25, 30, 40, 50, // Position values
        };
        ulong[] positionValuesCompressedIndices = {
            0x7777777777777777, 0x7777777777777777, 0x7777777777777777, 0x7777777777777777, // Compressed opening and endgame position indices for Null piece types (All squares evaluates to 0)
            0x7777FFFF9ABD889C, 0x777B865789937777, 0xFFFFEEEEDDDDBBBB, 0x9999888866667777, // Compressed opening and endgame position indices for Pawn piece types
            0x01221378289A27AB, 0x27AB289A13780122, 0x01221356259A26AB, 0x26AB259A13560122, // Compressed opening and endgame position indices for Knight piece types
            0x3555577757895889, 0x5799599958773555, 0x355557775799579D, 0x579D579957773555, // Compressed opening and endgame position indices for Bishop piece types
            0x7777899967776777, 0x6777677767777778, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF, // Compressed opening and endgame position indices for Rook piece types
            0x3556577757886789, 0x7789588857873556, 0x3556577757886789, 0x6789578857773556, // Compressed opening and endgame position indices for Queen piece types
            0x2110211021102110, 0x32215433BB77BD97, 0x0122135715BD25DE, 0x25DE15BD12770112, // Compressed opening and endgame position indices for King piece types
        };
        
        ////////////////////////////////////////////////////////////////////// Functions /////////////////////////////////////////////////////////////////////////

        // I have defined all the functions locally because each static keyword was consuming brain capacity (Same for the constants).
        // Also this will help me develop my next version of the bot.
        int DecompressData(int compressedDataOffset, int dataIndex) =>
            pieceValuesAndPositionIndices[((positionValuesCompressedIndices[compressedDataOffset] >> (60 - 4 * dataIndex)) & 0xF) + 7];

        int EvaluateBoard()
        {
            int score = 0, pieceScoreAbs = 0;
            foreach (PieceList pieceList in board.GetAllPieceLists())
                pieceScoreAbs += pieceList.Count * pieceValuesAndPositionIndices[(int)pieceList.TypeOfPieceInList];
            ulong allPiecesBB = board.AllPiecesBitboard;
            while (allPiecesBB != 0)
            {
                int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB)
                , pieceColorSign = -1;
                Square square = new(squareIndex);
                Piece piece = board.GetPiece(square);
                if (piece.IsWhite) // Flipping the square horizontally for white pieces as the array index is mirror to square index
                {
                    squareIndex ^= 0x38; // Flip the square vertically
                    pieceColorSign = 1;
                }
                // Adding the piece value to the score
                score += pieceColorSign * pieceValuesAndPositionIndices[(int)piece.PieceType];
                // Extracting the data from the compressed data and adding to score
                int file = squareIndex % 8;
                int dataIndex = (squareIndex + Min(file, 14 - 3 * file)) / 2; // 4 * rank + Min(file, 7 - file) simplified to this formula.
                int compressedDataOffset = 4 * (int)piece.PieceType + dataIndex / 16;
                dataIndex %= 16;
                int openingScore = DecompressData(compressedDataOffset, dataIndex);
                // In the opening, the more the king is surrounded by pawns of same color, the safer it is.
                if (piece.IsKing && BitboardHelper.SquareIsSet(0xFFFF_0000_0000_FFFF, square)) {
                    openingScore += 20 * BitboardHelper.GetNumberOfSetBits(
                        BitboardHelper.GetKingAttacks(square) &
                        board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
                    );
                }
                // The score is interpolated between the opening position score and the endgame position score
                // with respect to the total absolute piece values on the board defined by the pieceScoreAbs variable
                score += pieceColorSign * (
                    pieceScoreAbs * openingScore +
                    (8000 - pieceScoreAbs) * DecompressData(compressedDataOffset + 2, dataIndex)
                ) / 8000;
            }
            return board.IsWhiteToMove ? score : -score;
        }

        int ScoreMove(Move move, ushort ttBestMoveRawValue)
        {
            if (ply == 0 && move == bestMove)
                return 99_000;
            if (move.RawValue == ttBestMoveRawValue)
                return 98_000;
            int pieceTypeTimesHundred = 100 * (int)move.MovePieceType;
            if (move.IsCapture)
                return 97_000 - pieceTypeTimesHundred + (int)move.CapturePieceType;
            for (int i = 0; i < numKillerMoves; i++)
                if (move == killerMoves[ply, i])
                    return 96_000 - i;
            if (move.IsPromotion)
                return 95_000 + (int)move.PromotionPieceType;
            if (move.IsCastles)
                return 94_000;
            return Min(move.TargetSquare.File, 7 - move.TargetSquare.File) - pieceTypeTimesHundred;
        }

        void WriteTranspositionTable(int depth, int score, byte flag, ushort bestMoveRawValue)
        {
            ulong index = board.ZobristKey % transpositionTableSize;
            (ulong ttHash, byte ttDepth, short ttScore, _, _) = transpositionTable[index];
            // If the value is not from quiescence search (depth != 0) and the depth <= the current depth and the score is not a mate score (<= 24000) then overwrite the entry
            if (depth != 0 && Abs(ttScore) <= 24000 && (ttHash != board.ZobristKey || ttDepth <= depth))
                transpositionTable[index] = (board.ZobristKey, (byte)depth, (short)score, flag, bestMoveRawValue);
        }

        int AlphaBetaAndQuiescence(int depth, int alpha = -30000, int beta = 30000) // Merged Quiescence and AlphaBeta into single function (depth == 0 means Quiescence search)
        {
            int mate_score = 25000 - ply, score = -mate_score;
            if (board.IsInCheck() && depth > 1)
                depth++;
            depth = Max(depth, 0);
            bool isNotQuiescenceSearch = depth != 0;
            if (board.IsInCheckmate() && isNotQuiescenceSearch)
                return score;
            if (board.IsDraw())
                return 0;
            // Mate distance pruning (copied form Weiawaga chess engine)
            alpha = Max(alpha, -mate_score);
            beta = Min(beta, mate_score - 1);
            if (alpha >= beta)
                return alpha;
            // Transposition Table Lookup
            (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, ushort ttBestMoveRawValue) = transpositionTable[board.ZobristKey % transpositionTableSize];
            if (ply != 0 && ttHash == board.ZobristKey && ttDepth >= depth && isNotQuiescenceSearch)
            {
                if (ttFlag == 0)
                    return ttScore;
                if (ttFlag == 1 && ttScore <= alpha)
                    return alpha;
                if (ttFlag == 2 && ttScore >= beta)
                    return beta;
            }
            if (ttHash != board.ZobristKey)
                ttBestMoveRawValue = Move.NullMove.RawValue;
            nodes++; // #DEBUG
            // Null move pruning
            if (depth > 2 && Abs(beta) <= 24000) // No need to add the condition "&& isNotQuiescenceSearch" as depth > 2 is mentioned
                if (board.TrySkipTurn())
                {
                    ply++;
                    int nullMoveReductionScore = -AlphaBetaAndQuiescence(depth - 1 - Max(2, depth / 2), -beta, 1 - beta);
                    board.UndoSkipTurn();
                    ply--;
                    if (nullMoveReductionScore >= beta)
                        return beta;
                }
            if (!isNotQuiescenceSearch)
            {
                score = EvaluateBoard();
                seldepth = Max(seldepth, ply); // #DEBUG
                if (score >= beta)
                    return beta;
                alpha = Max(alpha, score);
            }
            var moves = board.GetLegalMoves(!isNotQuiescenceSearch);
            Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMoveRawValue) - ScoreMove(a, ttBestMoveRawValue));
            // Make sure that the best move is not an illegal move. This is done in root node only.
            if (ply == 0)
                bestMove = moves[0];
            byte flag = 1;
            // bool foundPV = false;
            foreach (Move move in moves)
            {
                board.MakeMove(move);
                ply++;
                // bool searchNormal = true;
                // // Principal Variation Search
                // if (foundPV && isNotQuiescenceSearch)
                // {
                //     score = -AlphaBetaAndQuiescence(depth - 1, -alpha - 1, -alpha);
                //     searchNormal = score > alpha && score < beta;
                // }
                // if (searchNormal)
                    score = -AlphaBetaAndQuiescence(depth - 1, -beta, -alpha);
                board.UndoMove(move);
                ply--;
                if (timer.MillisecondsElapsedThisTurn > timePerMove)
                    return 0;
                if (score > alpha)
                {
                    alpha = score;
                    // foundPV = true;
                    flag = 0;
                    // Update the best move in root node only
                    if (ply == 0)
                        bestMove = move;
                    if (alpha >= beta)
                    {
                        if (!move.IsCapture) // no need to add "&& isNotQuiescenceSearch" here because in Quiescence search, only captures are searched
                        {
                            int start = numKillerMoves - 1; // #DEBUG
                            for (int i = start; i > 0; i--)
                                killerMoves[ply, i] = killerMoves[ply, i-1];
                            killerMoves[ply, 0] = move;
                        }
                        WriteTranspositionTable(depth, beta, 2, move.RawValue);
                        return beta;
                    }
                }
            }
            WriteTranspositionTable(depth, alpha, flag, ttBestMoveRawValue);
            return alpha;
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        Console.WriteLine($"Position: {board.GetFenString()}"); // #DEBUG
        do
        { // #DEBUG
            seldepth = 0; // #DEBUG
            int score = // #DEBUG
            AlphaBetaAndQuiescence(depth++);
            Console.WriteLine($"info depth {depth - 1} seldepth {seldepth} nodes {nodes} nps {(1000 * nodes) / (ulong)Max(1, timer.MillisecondsElapsedThisTurn)} time {timer.MillisecondsElapsedThisTurn} score {score}"); // #DEBUG
        } // #DEBUG
        while (timer.MillisecondsElapsedThisTurn < timePerMove && depth <= 100); // 100 is the maximum depth
        Console.WriteLine($"Best {bestMove}"); // #DEBUG
        return bestMove;
    }
    }
}