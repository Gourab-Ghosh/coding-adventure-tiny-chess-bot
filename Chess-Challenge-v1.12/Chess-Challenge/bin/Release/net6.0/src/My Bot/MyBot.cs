// # Pawn (Y-symmetric)
//  0,  0,  0,  0,  0,  0,  0,  0,
//  50, 50, 50, 50, 50, 50, 50, 50,
//  10, 10, 20, 30, 30, 20, 10, 10,
//  5,  5, 10, 25, 25, 10,  5,  5,
//  0,  0,  0, 20, 20,  0,  0,  0,
//  5, -5,-10,  0,  0,-10, -5,  5,
//  5, 10, 10,-20,-20, 10, 10,  5,
//  0,  0,  0,  0,  0,  0,  0,  0,

// # Knight (Y-symmetric)
// -50,-40,-30,-30,-30,-30,-40,-50,
// -40,-20,  0,  0,  0,  0,-20,-40,
// -30,  0, 10, 15, 15, 10,  0,-30,
// -30,  5, 15, 20, 20, 15,  5,-30,
// -30,  0, 15, 20, 20, 15,  0,-30,
// -30,  5, 10, 15, 15, 10,  5,-30,
// -40,-20,  0,  5,  5,  0,-20,-40,
// -50,-40,-30,-30,-30,-30,-40,-50,

// # Bishop (Y-symmetric)
// -20,-10,-10,-10,-10,-10,-10,-20,
// -10,  0,  0,  0,  0,  0,  0,-10,
// -10,  0,  5, 10, 10,  5,  0,-10,
// -10,  5,  5, 10, 10,  5,  5,-10,
// -10,  0, 10, 10, 10, 10,  0,-10,
// -10, 10, 10, 10, 10, 10, 10,-10,
// -10,  5,  0,  0,  0,  0,  5,-10,
// -20,-10,-10,-10,-10,-10,-10,-20,

// # Rook (Y-symmetric)
//  0,  0,  0,  0,  0,  0,  0,  0,
//  5, 10, 10, 10, 10, 10, 10,  5,
// -5,  0,  0,  0,  0,  0,  0, -5,
// -5,  0,  0,  0,  0,  0,  0, -5,
// -5,  0,  0,  0,  0,  0,  0, -5,
// -5,  0,  0,  0,  0,  0,  0, -5,
// -5,  0,  0,  0,  0,  0,  0, -5,
//  0,  0,  0,  5,  5,  0,  0,  0,

// # Queen (XY-symmetric)
// -20,-15,-10, -5, -5,-10,-15,-20,
// -15,  0,  0,  0,  0,  0,  0,-15,
// -10,  0,  5,  5,  5,  5,  0,-10,
//  -5,  0,  5,  5,  5,  5,  0, -5,
//  -5,  0,  5,  5,  5,  5,  0, -5,
// -10,  0,  5,  5,  5,  5,  0,-10,
// -15,  0,  0,  0,  0,  0,  0,-15,
// -20,-15,-10, -5, -5,-10,-15,-20,

// # King Opening (Y-symmetric)
// -30,-40,-40,-50,-50,-40,-40,-30,
// -30,-40,-40,-50,-50,-40,-40,-30,
// -30,-40,-40,-50,-50,-40,-40,-30,
// -30,-40,-40,-50,-50,-40,-40,-30,
// -20,-30,-30,-40,-40,-30,-30,-20,
// -10,-20,-20,-20,-20,-20,-20,-10,
//  20, 20,  0,  0,  0,  0, 20, 20,
//  20, 30, 10,  0,  0, 10, 30, 20,

// # King Endgame (Y-symmetric)
// -50,-40,-30,-20,-20,-30,-40,-50,
// -30,-20,-10,  0,  0,-10,-20,-30,
// -30,-10, 20, 30, 30, 20,-10,-30,
// -30,-10, 30, 40, 40, 30,-10,-30,
// -30,-10, 30, 40, 40, 30,-10,-30,
// -30,-10, 20, 30, 30, 20,-10,-30,
// -30,-30,  0,  0,  0,  0,-30,-30,
// -50,-30,-30,-30,-30,-30,-30,-50,

using ChessChallenge.API;
using System;
// using System.Linq;
// using System.Numerics;
// using System.Collections.Generic;
using static System.Math;

public class MyBot : IChessBot
{
    static int timePerMove, _nodes, ply, numKillerMoves = 5;
    static ulong transpositionTableSize = 1000000;
    static int[] pieceValuesAndPositionIndices = {
        0, 100, 320, 330, 500, 900, 0, // Piece values (King is the most useless piece in chess so it's value is 0 :p)
        -50, -40, -30, -20, -15, -10, -5, 0, 5, 10, 15, 20, 25, 30, 40, 50, // Position values
    };
    static ulong[] positionValuesCompressedIndices = {
        0x7777777777777777, 0x7777777777777777, // Null piece position indices which all evaluates to 0 (Just to adjust the array index easily)
        0x7777ffff99bd889c, 0x777b865789937777, // Pawn position indices
        0x1221377279a28ab, 0x27ab289a13780122, // Knight position indices
        0x3555577757895889, 0x5799599958773555, // Bishop position indices
        0x7777899967776777, 0x6777677767777778, // Rook position indices
        0x3456477757886788, 0x6788578847773456, // Queen position indices
        0x2110211021102110, 0x32215333bb77bd97, // King opening position indices
        0x123235725bd25de, 0x25de25bd22770222, // King endgame position indices
    };
    static Board board;
    static Timer timer;
    static Move[,] killerMoves;
    static Move bestMove;
    static (ulong, byte, short, byte, ushort)[] transpositionTable; // (hash, depth, score, flag, bestMoveRawValue)

    static int ScoreMove(Move move, ushort ttBestMoveRawValue)
    {
        if (move.RawValue == ttBestMoveRawValue)
            return 110_000;
        if (move.Equals(bestMove))
            return 100_000;
        int maxPieceValue = 100 * (int) move.MovePieceType;
        if (move.IsCapture)
            return 99_000 - maxPieceValue + (int) move.CapturePieceType;
        for (int i = 0; i < numKillerMoves; i++)
            if (move.Equals(killerMoves[ply, i]))
                return 98_000 - i;
        if (move.IsPromotion)
            return 97_000 + (int) move.PromotionPieceType;
        if (move.IsCastles)
            return 96_000;
        board.MakeMove(move);
        bool givesCheck = board.IsInCheck();
        board.UndoMove(move);
        // If the move gives check and is not an important move then search it at the last as checks causes check extensions which increases the search time
        if (givesCheck)
            return -100_000;
        return -maxPieceValue - (int) Min(move.TargetSquare.File, 7 - move.TargetSquare.File);
    }

    static Move[] GetSortedLegalMoves(ushort ttBestMoveRawValue, bool capturesOnly = false)
    {
        Move[] moves = board.GetLegalMoves(capturesOnly);
        Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMoveRawValue) - ScoreMove(a, ttBestMoveRawValue));
        return moves;
    }

    static int GetPositionScore(int compressedDataOffset, int dataIndex)
    {
        return pieceValuesAndPositionIndices[(int) ((positionValuesCompressedIndices[compressedDataOffset] >> (int) ((15 - dataIndex) * 4)) & 0xF) + 7];
    }

    // static int Popcount(ulong x)
    // {
    //     return BitboardHelper.GetNumberOfSetBits(x);
    // }

    static int EvaluateBoard()
    {
        // Decrease the score by 5 for each piece in the backranks
        int score = 5 * (BitboardHelper.GetNumberOfSetBits(board.BlackPiecesBitboard & 0xFF00_0000_0000_00FF) - BitboardHelper.GetNumberOfSetBits(board.WhitePiecesBitboard & 0xFF00_0000_0000_00FF));
        int pieceScoreAbs = 0;
        foreach (PieceList pieceList in board.GetAllPieceLists())
            pieceScoreAbs += pieceList.Count * pieceValuesAndPositionIndices[(int) pieceList[0].PieceType];
        int positionScore = 0;
        ulong allPiecesBB = board.AllPiecesBitboard;
        while (allPiecesBB != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB);
            Piece piece = board.GetPiece(new Square(squareIndex));
            int sign = -1;
            if (piece.IsWhite) // Flipping the square horizontally for white pieces as the array index is mirror to square index
            {
                squareIndex ^= 0x38; // Flip the square vertically
                sign = 1;
            }
            // Adding the piece value to the score
            score += sign * pieceValuesAndPositionIndices[(int) piece.PieceType];
            // Extracting the data from the compressed data
            Square square = new Square(squareIndex);
            int dataIndex = Min(square.File, 7-square.File) + 4 * square.Rank;
            int compressedDataOffset = 2 * (int) piece.PieceType + dataIndex / 16;
            dataIndex %= 16;
            int currPiecePositionScore = GetPositionScore(compressedDataOffset, dataIndex);
            if (piece.PieceType == PieceType.King)
                // Interpolating king position score between opening and endgame position scores
                currPiecePositionScore = (int) (pieceScoreAbs * currPiecePositionScore + (8000 - pieceScoreAbs) * GetPositionScore(compressedDataOffset + 2, dataIndex)) / 8000;
            positionScore += sign * currPiecePositionScore;
        }
        score += positionScore / 3;
        if (board.IsWhiteToMove)
            return score;
        return -score;
    }

    static void WriteTranspositionTable(byte depth, short score, byte flag, ushort bestMoveRawValue) {
        ulong index = board.ZobristKey % transpositionTableSize;
        (ulong, byte, short, byte, ushort) ttEntry = transpositionTable[index];
        // If the value is from quiescence search or the depth is greater than the current depth or the score is a mate score (>= 24000) then don't overwrite the entry
        if (depth == 0 || (ttEntry.Item1 == board.ZobristKey && (ttEntry.Item2 > depth || Abs(ttEntry.Item3) >= 24000)))
            return;
        transpositionTable[index] = (board.ZobristKey, depth, score, flag, bestMoveRawValue);
    }

    static int AlphaBeta(int depth, int alpha = -30000, int beta = 30000) // Merged Quiescence and AlphaBeta into single function (depth == 0 means Quiescence search)
    {
        if (board.IsInCheck() && depth > 0)
            depth++;
        depth = Max(depth, 0);
        if (board.IsDraw())
            return 0;
        // Mate distance pruning
        int mate_score = 25000 - ply;
        if (mate_score < beta)
        {
            beta = mate_score;
            if (alpha >= mate_score)
                return mate_score;
        }
        int score = -mate_score;
        if (board.IsInCheckmate() && depth != 0)
            return score;
        ulong hash = board.ZobristKey;
        (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, ushort ttBestMoveRawValue) = transpositionTable[hash % transpositionTableSize];
        if (ply != 0 && ttHash == hash && ttDepth >= depth && depth != 0)
        {
            if (ttFlag == 0)
                return ttScore;
            if (ttFlag == 1 && ttScore <= alpha)
                return alpha;
            if (ttFlag == 2 && ttScore >= beta)
                return beta;
        }
        if (ttHash != hash)
            ttBestMoveRawValue = Move.NullMove.RawValue;
        if (depth == 0)
        {
            score = EvaluateBoard();
            if (score >= beta)
                return beta;
            alpha = Max(alpha, score);
        }
        _nodes++;
        byte flag = 1;
        foreach (Move move in GetSortedLegalMoves(ttBestMoveRawValue, depth == 0))
        {
            board.MakeMove(move);
            ply++;
            score = -AlphaBeta(depth - 1, -beta, -alpha);
            board.UndoMove(move);
            ply--;
            if (timer.MillisecondsElapsedThisTurn > timePerMove)
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
                    WriteTranspositionTable((byte) depth, (short) beta, 2, move.RawValue);
                    return beta;
                }
            }
        }
        WriteTranspositionTable((byte) depth, (short) alpha, flag, ttBestMoveRawValue);
        return alpha;
    }

    public Move Think(Board board_, Timer timer_)
    {
        _nodes = 0;
        board = board_;
        timer = timer_;
        ply = 0;
        bestMove = Move.NullMove;
        killerMoves = new Move[150, numKillerMoves]; // 150 is the maximum ply
        transpositionTable = new (ulong, byte, short, byte, ushort)[transpositionTableSize];
        timePerMove = timer.MillisecondsRemaining / 30;
        // timePerMove = 1000;
        int depth = 1;
        // while (timer.MillisecondsElapsedThisTurn < timePerMove && depth <= 100) // 100 is the maximum depth
        //     AlphaBeta(depth++);
        while (timer.MillisecondsElapsedThisTurn < timePerMove && depth <= 100) // 100 is the maximum depth
        {
            int score = AlphaBeta(depth++);
            Console.WriteLine($"{depth - 1}, {_nodes}, {score}");
        }
        // Console.WriteLine(EvaluateBoard());
        // foreach (Move move in GetSortedLegalMoves(board))
        // {
        //     Console.Write(move);
        //     Console.Write(" ");
        // }
        if (bestMove.IsNull)
            bestMove = GetSortedLegalMoves(Move.NullMove.RawValue)[0];
        return bestMove;
    }
}