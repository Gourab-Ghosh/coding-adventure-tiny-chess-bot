using ChessChallenge.API;
using System;
// using System.Linq;
// using System.Numerics;
// using System.Collections.Generic;
using static System.Math;

public class MyBot : IChessBot
{
    static int timePerMove = 1000;
    static int nodes;
    static int[] pieceValues = {0, 100, 320, 330, 500, 900, 20000};
    static ulong[] positionValuesCompressedIndices = {
        0, 0,
        0x7777ffff99bd889c, 0x777b865789937777,
        0x1221377279a28ab, 0x27ab289a13780122,
        0x3555577757895889, 0x5799599958773555,
        0x7777899967776777, 0x6777677767777778,
        0x3456477757886788, 0x6788578847773456,
        0x2110211021102110, 0x32215333bb77bd97,
        0x123235725bd25de, 0x25de25bd22770222,
    };
    // static int[] positionValues = { 0, 5, 10, 20 };
    static int[] positionValues = {-50, -40, -30, -20, -15, -10, -5, 0, 5, 10, 15, 20, 25, 30, 40, 50};
    static Board board;
    static Timer timer;

    static int numKillerMoves = 5;
    static Move[] killerMoves;
    static Move bestMove;
    static ulong transpositionTableSize = 1000000;
    static (ulong, byte, short, byte, Move)[] transpositionTable = new (ulong, byte, short, byte, Move)[transpositionTableSize]; // (hash, depth, score, flag, bestMove)

    static int Popcount(ulong x)
    {
        return BitboardHelper.GetNumberOfSetBits(x);
    }

    static int ScoreMove(Move move, Move ttBestMove)
    {
        if (move.Equals(ttBestMove))
            return 110_000;
        if (move.Equals(bestMove))
            return 100_000;
        if (move.IsCapture)
            return 99_000 + 100 * (7 - (int) GetPiece(move.StartSquare).PieceType) + (int) GetPiece(move.TargetSquare).PieceType;
        for (int i = 0; i < numKillerMoves; i++)
            if (move.Equals(killerMoves[i]))
                return 98_000 - i;
        if (move.IsPromotion)
            return 97_000 + (int) move.PromotionPieceType;
        board.MakeMove(move);
        bool givesCheck = board.IsInCheck();
        board.UndoMove(move);
        if (givesCheck)
            return -100_000;
        return -board.GetLegalMoves().Length;
    }

    static Move[] GetSortedLegalMoves(Move ttBestMove, bool capturesOnly = false)
    {
        Move[] moves = board.GetLegalMoves(capturesOnly);
        Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMove) - ScoreMove(a, ttBestMove));
        return moves;
    }

    // static int EvaluateBoard()
    // {
    //     int score = 0;
    //     for (int i = 1; i < 6; i++)
    //     {
    //         score += pieceValues[i] * Popcount(board.GetPieceBitboard((PieceType) i, true));
    //         score -= pieceValues[i] * Popcount(board.GetPieceBitboard((PieceType) i, false));
    //     }
    //     int position_score = 0;
    //     foreach ((ulong bitboard, int sign) in new[] {(board.WhitePiecesBitboard, 1), (board.BlackPiecesBitboard, -1)}) {
    //         ulong bb = bitboard;
    //         while (bb != 0)
    //         {
    //             Square square = new Square(BitboardHelper.ClearAndGetIndexOfLSB(ref bb));
    //             position_score += sign * positionValues[Min(Min(square.Rank, 7-square.Rank), Min(square.File, 7-square.File))] * (6 - (int) GetPiece(square).PieceType);
    //         }
    //     }
    //     score += position_score / 5;
    //     if (board.IsWhiteToMove)
    //         return score;
    //     return -score;
    // }

    static Piece GetPiece(Square square)
    {
        for (int i = 1; i < 6; i++)
            foreach (bool isWhite in new[] {true, false})
                if ((board.GetPieceBitboard((PieceType) i, isWhite) & (ulong) (1 << square.Index)) != 0)
                    return new Piece((PieceType) i, isWhite, square);
        return new Piece((PieceType) 0, false, square);
    }

    static int EvaluateBoard()
    {
        int piece_score = 0;
        int position_score = 0;
        ulong allPiecesBB = board.AllPiecesBitboard;
        while (allPiecesBB != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB);
            Piece piece = GetPiece(new Square(squareIndex));
            int sign = piece.IsWhite ? 1 : -1;
            piece_score += sign * pieceValues[(int) piece.PieceType];
            if (piece.IsWhite) // Flipping the square horizontally for white pieces as the array index is mirror to square index
                squareIndex ^= 0x38; // Flip the square vertically
            // Extracting the data from the compressed data
            Square square = new Square(squareIndex);
            int dataIndex = Min(square.File, 7-square.File) + 4 * square.Rank;
            int compressedDataOffset = 2 * (int) piece.PieceType + dataIndex / 16;
            dataIndex %= 16;
            position_score += (piece.IsWhite ? 1 : -1) * positionValues[(int) ((positionValuesCompressedIndices[compressedDataOffset] >> (int) ((15 - dataIndex) * 4)) & 0xF)] * (11 - (int) piece.PieceType);
        }
        int score = piece_score;
        if (board.IsWhiteToMove)
            return score;
        return -score;
    }

    static void WriteTranspositionTable(byte depth, short score, byte flag, Move BestMove) {
        ulong index = board.ZobristKey % transpositionTableSize;
        (ulong, byte, short, byte, Move) ttEntry = transpositionTable[index];
        if (ttEntry.Item1 == board.ZobristKey && (ttEntry.Item2 > depth || Abs(ttEntry.Item3) >= 24000))
            return;
        transpositionTable[index] = (board.ZobristKey, depth, score, flag, BestMove);
    }

    static int AlphaBeta(int depth, int ply = 0, int alpha = -30000, int beta = 30000)
    {
        if (board.IsDraw())
            return 0;
        int mate_score = 25000 - ply;
        // Mate distance pruning
        if (mate_score < beta)
        {
            beta = mate_score;
            if (alpha >= mate_score)
                return mate_score;
        }
        int score = -mate_score;
        if (board.IsInCheckmate())
            return score;
        // bool isInCheck = board.IsInCheck();
        if (board.IsInCheck())
            depth++;
        depth = Max(depth, 0);
        ulong hash = board.ZobristKey;
        (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, Move ttBestMove) = transpositionTable[hash % transpositionTableSize];
        if (ply != 0 && ttHash == hash && ttDepth >= depth)
        {
            if (ttFlag == 0)
                return ttScore;
            if (ttFlag == 1 && ttScore <= alpha)
                return alpha;
            if (ttFlag == 2 && ttScore >= beta)
                return beta;
        }
        if (ttHash != hash)
            ttBestMove = Move.NullMove;
        if (depth == 0)
            return Quiescence(ply, alpha, beta);
        nodes++;
        Move[] moves = GetSortedLegalMoves(ttBestMove);
        byte flag = 1;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            score = -AlphaBeta(depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);
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
                            killerMoves[i] = killerMoves[i-1];
                        killerMoves[0] = move;
                    }
                    WriteTranspositionTable((byte) depth, (short) beta, 2, move);
                    return beta;
                }
            }
        }
        WriteTranspositionTable((byte) depth, (short) alpha, flag, ttBestMove);
        return alpha;
    }

    static int Quiescence(int ply, int alpha, int beta)
    {
        int score = EvaluateBoard();
        if (score >= beta)
            return beta;
        nodes++;
        if (score > alpha)
            alpha = score;
        foreach (Move move in GetSortedLegalMoves(Move.NullMove, true))
        {
            board.MakeMove(move);
            score = -Quiescence(ply + 1, -beta, -alpha);
            board.UndoMove(move);
            if (score >= beta)
                return beta;
            if (score > alpha)
                alpha = score;
        }
        return alpha;
    }

    public Move Think(Board board_, Timer timer_)
    {
        nodes = 0;
        board = board_;
        timer = timer_;
        bestMove = Move.NullMove;
        killerMoves = new Move[numKillerMoves];
        int depth = 1;
        while (timer.MillisecondsElapsedThisTurn < timePerMove)
        {
            if (depth > 150)
                break;
            int score = AlphaBeta(depth++);
            Console.WriteLine($"{depth - 1}, {nodes}, {score}");
        }
        // Console.WriteLine(EvaluateBoard());
        // foreach (Move move in GetSortedLegalMoves(board))
        // {
        //     Console.Write(move);
        //     Console.Write(" ");
        // }
        return bestMove;
    }
}