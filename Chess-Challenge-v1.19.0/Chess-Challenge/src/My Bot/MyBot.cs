using ChessChallenge.API;
using System;
// using System.Linq;
// using System.Numerics;
// using System.Collections.Generic;
using static System.Math;

public class MyBot : IChessBot
{
    static int timePerMove, ply;
    static int numKillerMoves = 3; // #DEBUG
    static int nodes; // #DEBUG
    // static ulong transpositionTableSize = 1000000; // #DEBUG
    static ulong transpositionTableSize = 16777216; // 256 MB #DEBUG
    static int[] pieceValuesAndPositionIndices = {
        0, 100, 320, 330, 500, 900, 0, // Piece values (King is the most useless piece in chess so it's value is 0 :p)
        -50, -40, -30, -20, -15, -10, -5, 0, 5, 10, 15, 20, 25, 30, 40, 50, // Position values
    };
    static ulong[] positionValuesCompressedIndices = {
        0x7777777777777777, 0x7777777777777777, 0x7777777777777777, 0x7777777777777777, // Null piece position indices which all evaluates to 0 (Just to adjust the array index easily)
        0x7777FFFF9ABD889C, 0X777B865789937777, 0XFFFFEEEEDDDDBBBB, 0X9999888866667777, // Pawn opening and endgame position indices
        0x1221378289A27AB, 0X27AB289A13780122, 0X1221356259A26AB, 0X26AB259A13560122, // Knight opening and endgame position indices
        0x3555577757895889, 0X5799599958773555, 0X355557775799579D, 0X579D579957773555, // Bishop opening and endgame position indices
        0x7777899967776777, 0X6777677767777778, 0XFFFFFFFFFFFFFFFF, 0XFFFFFFFFFFFFFFFF, // Rook opening and endgame position indices
        0x3556577757886789, 0X7789588857873556, 0X3556577757886789, 0X6789578857773556, // Queen opening and endgame position indices
        0x2110211021102110, 0X32215433BB77BD97, 0X122135715BD25DE, 0X25DE15BD12770122, // King opening and endgame position indices
    };
    static Board board_;
    static Timer timer_;
    static Move[,] killerMoves;
    static Move bestMove;
    static (ulong, byte, short, byte, ushort)[] transpositionTable = new (ulong, byte, short, byte, ushort)[transpositionTableSize]; // (hash, depth, score, flag, bestMoveRawValue)

    static int ScoreMove(Move move, ushort ttBestMoveRawValue)
    {
        if (ply == 0 && move == bestMove)
            return 110_000;
        if (move.RawValue == ttBestMoveRawValue)
            return 100_000;
        int pieceTypeTimesHundred = 100 * (int) move.MovePieceType;
        if (move.IsCapture)
            return 99_000 - pieceTypeTimesHundred + (int) move.CapturePieceType;
        for (int i = 0; i < numKillerMoves; i++)
            if (move == killerMoves[ply, i])
                return 98_000 - i;
        if (move.IsPromotion)
            return 97_000 + (int) move.PromotionPieceType;
        if (move.IsCastles)
            return 96_000;
        return Min(move.TargetSquare.File, 7 - move.TargetSquare.File) - pieceTypeTimesHundred;
    }

    static int GetPositionScore(int compressedDataOffset, int dataIndex) =>
        pieceValuesAndPositionIndices[(int) ((positionValuesCompressedIndices[compressedDataOffset] >> (int) ((15 - dataIndex) * 4)) & 0xF) + 7];

    // static int GetSquareDistance(Square square1, Square square2)
    // {
    //     return Max(Abs(square1.Rank - square2.Rank), Abs(square1.File - square2.File));
    // }

    static int EvaluateBoard()
    {
        // Decrease the score by 5 for each piece in the backranks
        int score = 5 * (BitboardHelper.GetNumberOfSetBits(board_.BlackPiecesBitboard & 0xFF00_0000_0000_00FF) - BitboardHelper.GetNumberOfSetBits(board_.WhitePiecesBitboard & 0xFF00_0000_0000_00FF))
        , pieceScoreAbs = 0;
        foreach (PieceList pieceList in board_.GetAllPieceLists())
            pieceScoreAbs += pieceList.Count * pieceValuesAndPositionIndices[(int) pieceList.TypeOfPieceInList];
        ulong allPiecesBB = board_.AllPiecesBitboard;
        while (allPiecesBB != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB), sign = -1;
            Piece piece = board_.GetPiece(new Square(squareIndex));
            if (piece.IsWhite) // Flipping the square horizontally for white pieces as the array index is mirror to square index
            {
                squareIndex ^= 0x38; // Flip the square vertically
                sign = 1;
            }
            // Adding the piece value to the score
            score += sign * pieceValuesAndPositionIndices[(int) piece.PieceType];
            // Extracting the data from the compressed data and adding to score
            int file = squareIndex % 8;
            int dataIndex = (squareIndex + Min(file, 14 - 3*file)) / 2; // 4*rank + Min(file, 7 - file) simplified to this formula.
            int compressedDataOffset = 4 * (int) piece.PieceType + dataIndex / 16;
            dataIndex %= 16;
            // int currPiecePositionScore = GetPositionScore(compressedDataOffset, dataIndex);
            // if (piece.PieceType == PieceType.King && (board_.GetPieceBitboard(PieceType.Pawn, piece.IsWhite) & ((ulong) 0x0101010101010101 << file)) == 0)
            //     currPiecePositionScore -= 30 * sign;
            // currPiecePositionScore = (int) (pieceScoreAbs * currPiecePositionScore + (8000 - pieceScoreAbs) * GetPositionScore(compressedDataOffset + 2, dataIndex)) / 8000;
            score += sign * (pieceScoreAbs * GetPositionScore(compressedDataOffset, dataIndex) + (8000 - pieceScoreAbs) * GetPositionScore(compressedDataOffset + 2, dataIndex)) / 8000;
        }
        // if (numPieces <= 10)
        //     score += positionScore / 2;
        // score += (score > 0 ? 5 : -5) * GetSquareDistance(board_.GetKingSquare(true), board_.GetKingSquare(false));
        return board_.IsWhiteToMove ? score : -score;
    }

    static void WriteTranspositionTable(int depth, int score, byte flag, ushort bestMoveRawValue) {
        ulong index = board_.ZobristKey % transpositionTableSize;
        (ulong, byte, short, byte, ushort) ttEntry = transpositionTable[index];
        // If the value is from quiescence search or the depth is greater than the current depth or the score is a mate score (>= 24000) then don't overwrite the entry
        if (depth == 0 || Abs(ttEntry.Item3) >= 24000 || (ttEntry.Item1 == board_.ZobristKey && ttEntry.Item2 > depth))
            return;
        transpositionTable[index] = (board_.ZobristKey, (byte) depth, (short) score, flag, bestMoveRawValue);
    }

    static int AlphaBetaAndQuiescence(int depth, int alpha = -30000, int beta = 30000) // Merged Quiescence and AlphaBeta into single function (depth == 0 means Quiescence search)
    {
        int mate_score = 25000 - ply, score = -mate_score;
        if (board_.IsInCheck() && depth > 1)
            depth++;
        depth = Max(depth, 0);
        if (board_.IsDraw())
            return 0;
        // Mate distance pruning
        if (mate_score < beta)
        {
            beta = mate_score;
            if (alpha >= mate_score)
                return mate_score;
        }
        if (board_.IsInCheckmate() && depth != 0)
            return score;
        (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, ushort ttBestMoveRawValue) = transpositionTable[board_.ZobristKey % transpositionTableSize];
        if (ply != 0 && ttHash == board_.ZobristKey && ttDepth >= depth && depth != 0)
        {
            if (ttFlag == 0)
                return ttScore;
            if (ttFlag == 1 && ttScore <= alpha)
                return alpha;
            if (ttFlag == 2 && ttScore >= beta)
                return beta;
        }
        if (ttHash != board_.ZobristKey)
            ttBestMoveRawValue = Move.NullMove.RawValue;
        if (depth > 2 && Abs(beta) <= 24000)
            if (board_.TrySkipTurn())
            {
                ply++;
                int nullMoveReductionScore = -AlphaBetaAndQuiescence(depth - 1 - Max(2, depth / 2), -beta, 1-beta);
                board_.UndoSkipTurn();
                ply--;
                if (nullMoveReductionScore >= beta)
                    return beta;
            }
        if (depth == 0)
        {
            score = EvaluateBoard();
            if (score >= beta)
                return beta;
            alpha = Max(alpha, score);
        }
        nodes++; // #DEBUG
        Move[] moves = board_.GetLegalMoves(depth == 0);
        Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMoveRawValue) - ScoreMove(a, ttBestMoveRawValue));
        if (ply == 0)
            bestMove = moves[0];
        byte flag = 1;
        foreach (Move move in moves)
        {
            board_.MakeMove(move);
            ply++;
            score = -AlphaBetaAndQuiescence(depth - 1, -beta, -alpha);
            board_.UndoMove(move);
            ply--;
            if (timer_.MillisecondsElapsedThisTurn > timePerMove)
                return 0;
            if (score > alpha)
            {
                alpha = score;
                flag = 0;
                if (ply == 0)
                    bestMove = move;
                if (alpha >= beta) {
                    if (!move.IsCapture)
                    {
                        for (int i = numKillerMoves - 1; i > 0; i--)
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

    public Move Think(Board board, Timer timer)
    {
        nodes = 0; // #DEBUG
        ply = 0;
        board_ = board;
        timer_ = timer;
        killerMoves = new Move[150, numKillerMoves]; // 150 is the maximum ply
        // transpositionTable = new (ulong, byte, short, byte, ushort)[transpositionTableSize];
        // timePerMove = Max(10, (timer_.MillisecondsRemaining - Max(0, timer_.OpponentMillisecondsRemaining - timer_.MillisecondsRemaining)) / 30);
        timePerMove = Max(10, timer_.MillisecondsRemaining / 30 + timer_.IncrementMilliseconds - 100);
        if (timer_.OpponentMillisecondsRemaining <= 60_000) // #Debug
            timePerMove = 100; // #DEBUG
        int depth = 1;
        Console.WriteLine($"Position: {board_.GetFenString()}"); // #DEBUG
        do
        { // #DEBUG
            int score = AlphaBetaAndQuiescence(depth++);
            Console.WriteLine($"info depth {depth - 1} nodes {nodes} nps {(1000 * nodes) / Max(1, timer_.MillisecondsElapsedThisTurn)} time {timer_.MillisecondsElapsedThisTurn} score {score}"); // #DEBUG
        } // #DEBUG
        while (timer_.MillisecondsElapsedThisTurn < timePerMove && depth <= 100); // 100 is the maximum depth
        Console.WriteLine($"Best {bestMove}"); // #DEBUG
        return bestMove;
    }
}

// 2b3k1/rp1nrpb1/p1qN1n1p/2P1p1p1/1P1pP3/3P1N1P/RBQRBPPK/8 b - - 1 21
// 8/1r3pbk/prq1bn1p/2p1p3/2PpP1n1/P2P1N2/RBQNBPP1/4R1K1 w - - 54 61
// 8/1r2rpbk/p2qbn1p/2p1p3/2PpP1n1/PN1P1N2/RBQ1BPP1/4R1K1 b - - 7 37