using System;
using System.Collections.Generic;

namespace PIDControlDemo.Models;

public class EngineSimulator
{
    // 非線形トルクカーブ: アクセル開度(%) → 定常回転数(rpm)
    private static readonly double[] ThrottlePoints = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
    private static readonly double[] RpmPoints = { 0, 150, 400, 750, 1150, 1550, 1900, 2200, 2500, 2750, 3000 };

    private readonly double _timeConstant; // 一次遅れ系の時定数(秒)
    private readonly double _sensorDelay;  // センサー遅延(秒)
    private readonly Queue<(double time, double rpm)> _sensorBuffer = new();
    private double _elapsedTime;

    public double ActualRpm { get; private set; }
    public double SensedRpm { get; private set; }

    public EngineSimulator(double timeConstant = 1.0, double sensorDelay = 2.0)
    {
        _timeConstant = timeConstant;
        _sensorDelay = sensorDelay;
    }

    public void Update(double deltaTime, double currentThrottle)
    {
        _elapsedTime += deltaTime;

        // 非線形カーブから定常回転数を算出
        double steadyStateRpm = InterpolateRpm(currentThrottle);

        // 一次遅れ系で実際の回転数を更新
        double alpha = 1.0 - Math.Exp(-deltaTime / _timeConstant);
        ActualRpm += (steadyStateRpm - ActualRpm) * alpha;

        // センサーバッファに記録
        _sensorBuffer.Enqueue((_elapsedTime, ActualRpm));

        // 遅延分だけ古いデータをセンサー値として出力
        while (_sensorBuffer.Count > 1)
        {
            var oldest = _sensorBuffer.Peek();
            if (_elapsedTime - oldest.time >= _sensorDelay)
            {
                SensedRpm = oldest.rpm;
                _sensorBuffer.Dequeue();
            }
            else
            {
                break;
            }
        }
    }

    private static double InterpolateRpm(double throttle)
    {
        throttle = Math.Clamp(throttle, 0.0, 100.0);

        for (int i = 0; i < ThrottlePoints.Length - 1; i++)
        {
            if (throttle <= ThrottlePoints[i + 1])
            {
                double t = (throttle - ThrottlePoints[i]) / (ThrottlePoints[i + 1] - ThrottlePoints[i]);
                return RpmPoints[i] + t * (RpmPoints[i + 1] - RpmPoints[i]);
            }
        }

        return RpmPoints[^1];
    }

    public void Reset()
    {
        ActualRpm = 0;
        SensedRpm = 0;
        _elapsedTime = 0;
        _sensorBuffer.Clear();
    }
}
