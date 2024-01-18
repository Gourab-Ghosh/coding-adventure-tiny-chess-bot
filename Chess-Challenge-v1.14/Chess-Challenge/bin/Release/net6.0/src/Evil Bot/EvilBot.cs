using ChessChallenge.API;
using System;
using static System.Math;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
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
    static Board board_;
    static Timer timer_;
    static Move[,] killerMoves;
    static Move bestMove;
    static (ulong, byte, short, byte, ushort)[] transpositionTable; // (hash, depth, score, flag, bestMoveRawValue)

    static int ScoreMove(Move move, ushort ttBestMoveRawValue)
    {
        if (move.RawValue == ttBestMoveRawValue)
            return 110_000;
        if (move == bestMove)
            return 100_000;
        int maxPieceValue = 100 * (int) move.MovePieceType;
        if (move.IsCapture)
            return 99_000 - maxPieceValue + (int) move.CapturePieceType;
        for (int i = 0; i < numKillerMoves; i++)
            if (move == killerMoves[ply, i])
                return 98_000 - i;
        if (move.IsPromotion)
            return 97_000 + (int) move.PromotionPieceType;
        if (move.IsCastles)
            return 96_000;
        return -maxPieceValue - (int) Min(move.TargetSquare.File, 7 - move.TargetSquare.File);
    }

    static Move[] GetSortedLegalMoves(ushort ttBestMoveRawValue, bool capturesOnly = false)
    {
        Move[] moves = board_.GetLegalMoves(capturesOnly);
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
        int score = 5 * (BitboardHelper.GetNumberOfSetBits(board_.BlackPiecesBitboard & 0xFF00_0000_0000_00FF) - BitboardHelper.GetNumberOfSetBits(board_.WhitePiecesBitboard & 0xFF00_0000_0000_00FF))
        , pieceScoreAbs = 0, positionScore = 0;
        ulong allPiecesBB = board_.AllPiecesBitboard;
        foreach (PieceList pieceList in board_.GetAllPieceLists())
            pieceScoreAbs += pieceList.Count * pieceValuesAndPositionIndices[(int) pieceList.TypeOfPieceInList];
        while (allPiecesBB != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB);
            Piece piece = board_.GetPiece(new Square(squareIndex));
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
        if (board_.IsWhiteToMove)
            return score;
        return -score;
    }

    static void WriteTranspositionTable(byte depth, short score, byte flag, ushort bestMoveRawValue) {
        ulong index = board_.ZobristKey % transpositionTableSize;
        (ulong, byte, short, byte, ushort) ttEntry = transpositionTable[index];
        // If the value is from quiescence search or the depth is greater than the current depth or the score is a mate score (>= 24000) then don't overwrite the entry
        if (depth == 0 || (ttEntry.Item1 == board_.ZobristKey && (ttEntry.Item2 > depth || Abs(ttEntry.Item3) >= 24000)))
            return;
        transpositionTable[index] = (board_.ZobristKey, depth, score, flag, bestMoveRawValue);
    }

    static int AlphaBeta(int depth, int alpha = -30000, int beta = 30000) // Merged Quiescence and AlphaBeta into single function (depth == 0 means Quiescence search)
    {
        int mate_score = 25000 - ply, score = -mate_score;
        if (board_.IsInCheck() && depth > 0)
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
            board_.MakeMove(move);
            ply++;
            score = -AlphaBeta(depth - 1, -beta, -alpha);
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
                    WriteTranspositionTable((byte) depth, (short) beta, 2, move.RawValue);
                    return beta;
                }
            }
        }
        WriteTranspositionTable((byte) depth, (short) alpha, flag, ttBestMoveRawValue);
        return alpha;
    }

    public Move Think(Board board, Timer timer)
    {
        _nodes = ply = 0;
        board_ = board;
        timer_ = timer;
        bestMove = Move.NullMove;
        killerMoves = new Move[150, numKillerMoves]; // 150 is the maximum ply
        transpositionTable = new (ulong, byte, short, byte, ushort)[transpositionTableSize];
        // timePerMove = timer_.MillisecondsRemaining / 30;
        timePerMove = 100;
        int depth = 1;
        // while (timer_.MillisecondsElapsedThisTurn < timePerMove && depth <= 100) // 100 is the maximum depth
        //     AlphaBeta(depth++);
        while (timer_.MillisecondsElapsedThisTurn < timePerMove && depth <= 100) // 100 is the maximum depth
        {
            int score = AlphaBeta(depth++);
            Console.WriteLine($"{depth - 1}, {_nodes}, {score}");
        }
        // Console.WriteLine(EvaluateBoard());
        // foreach (Move move in GetSortedLegalMoves(board_))
        // {
        //     Console.Write(move);
        //     Console.Write(" ");
        // }
        if (bestMove.IsNull)
            bestMove = GetSortedLegalMoves(Move.NullMove.RawValue)[0];
        return bestMove;
    }
    }
}