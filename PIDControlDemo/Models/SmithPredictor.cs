using System;
using System.Collections.Generic;

namespace PIDControlDemo.Models;

/// <summary>
/// スミス予測器（Smith Predictor）。
/// 内部にプラントモデル（一次遅れ系）を持ち、むだ時間を補償する。
/// PIDはむだ時間なしのプロセスに対して制御しているように振る舞える。
///
/// 構造:
///   PID入力 = setpoint - (sensedRpm - modelOutputDelayed + modelOutputImmediate)
///   = setpoint - sensedRpm + (modelOutputDelayed - modelOutputImmediate)
///   → むだ時間の影響をキャンセルし、PIDは一次遅れ系のみを見る
/// </summary>
public class SmithPredictor
{
    private readonly Queue<(double time, double value)> _delayBuffer = new();
    private double _elapsed;

    // 内部プラントモデルの状態
    private double _modelRpm;

    // パラメータ（エンジン特性に合わせて自動設定）
    public double ModelTimeConstant { get; set; } = 1.0;  // プラントの時定数(秒)
    public double DeadTime { get; set; } = 2.0;           // むだ時間(秒)
    public double ModelGain { get; set; } = 30.0;         // rpm/throttle% のゲイン(線形近似)

    /// <summary>
    /// スミス予測器の補正値を計算する。
    /// PIDの入力に使う「補正済みプロセス値」を返す。
    /// </summary>
    /// <param name="sensedRpm">実際のセンサー値（むだ時間あり）</param>
    /// <param name="controlOutput">PIDの出力（ボタン押下時間に変換前の値）</param>
    /// <param name="deltaTime">制御周期</param>
    /// <returns>補正済みのプロセス値（PIDのfeedbackとして使う）</returns>
    public double ComputeCorrectedPV(double sensedRpm, double controlOutput, double deltaTime)
    {
        _elapsed += deltaTime;

        // 内部モデル: 一次遅れ系でPID出力に対する応答を予測
        // controlOutput > 0 → rpmが上がる方向、< 0 → 下がる方向
        double modelTarget = _modelRpm + controlOutput * ModelGain * 0.01;
        modelTarget = Math.Clamp(modelTarget, 0, 3000);

        double alpha = 1.0 - Math.Exp(-deltaTime / ModelTimeConstant);
        _modelRpm += (modelTarget - _modelRpm) * alpha;

        double modelImmediate = _modelRpm;

        // むだ時間分遅延させたモデル出力
        _delayBuffer.Enqueue((_elapsed, _modelRpm));

        double modelDelayed = 0;
        while (_delayBuffer.Count > 1)
        {
            var oldest = _delayBuffer.Peek();
            if (_elapsed - oldest.time >= DeadTime)
            {
                modelDelayed = oldest.value;
                _delayBuffer.Dequeue();
            }
            else
            {
                break;
            }
        }
        if (_delayBuffer.Count == 1)
        {
            modelDelayed = _delayBuffer.Peek().value;
        }

        // スミス予測器の補正:
        // correctedPV = sensedRpm + (modelImmediate - modelDelayed)
        // これにより、PIDは「むだ時間なしの一次遅れ系」を制御する
        double correctedPV = sensedRpm + (modelImmediate - modelDelayed);

        return correctedPV;
    }

    public void Reset()
    {
        _modelRpm = 0;
        _elapsed = 0;
        _delayBuffer.Clear();
    }

    /// <summary>
    /// エンジン特性に基づいてパラメータを自動設定する。
    /// シミュレーターの既知パラメータから設定。
    /// </summary>
    public void AutoConfigure()
    {
        // EngineSimulator: timeConstant=1.0, sensorDelay=2.0
        // 非線形カーブの中間域(40-60%)での平均ゲイン: 約 (1900-1150)/(60-40) = 37.5 rpm/%
        ModelTimeConstant = 1.0;
        DeadTime = 2.0;
        ModelGain = 37.5;
    }
}
