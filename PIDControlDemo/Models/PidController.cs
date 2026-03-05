using System;

namespace PIDControlDemo.Models;

public class PidController
{
    public double Kp { get; set; } = 0.5;
    public double Ki { get; set; } = 0.1;
    public double Kd { get; set; } = 0.05;

    private double _integral;
    private double _previousError;
    private bool _hasFirstRun;

    private const double IntegralClamp = 1000.0;

    public double Compute(double setpoint, double processValue, double deltaTime)
    {
        if (deltaTime <= 0) return 0;

        double error = setpoint - processValue;

        // 積分項(アンチワインドアップ付き)
        _integral += error * deltaTime;
        _integral = Math.Clamp(_integral, -IntegralClamp, IntegralClamp);

        // 微分項
        double derivative = 0;
        if (_hasFirstRun)
        {
            derivative = (error - _previousError) / deltaTime;
        }
        _hasFirstRun = true;
        _previousError = error;

        return Kp * error + Ki * _integral + Kd * derivative;
    }

    public void Reset()
    {
        _integral = 0;
        _previousError = 0;
        _hasFirstRun = false;
    }
}
