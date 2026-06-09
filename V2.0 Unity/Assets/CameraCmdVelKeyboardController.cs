using UnityEngine;

public class CameraCmdVelKeyboardController : MonoBehaviour
{
    [Header("Linear cmd_vel")]
    public float linearStep = 5.0f;
    public float maxLinearSpeed = 100.0f; 

    [Header("Angular cmd_vel")]
    public float yawStepDeg = 10.0f;
    public float pitchStepDeg = 10.0f;
    public float maxAngularSpeedDeg = 90.0f;

    [Header("Current cmd_vel")]
    public Vector3 linearCmd = new Vector3(10f, 0f, 0f);
    public Vector3 angularCmd;

    private void Update()
    {
        UpdateCmdVelByKeyboard();
        ClampCmdVel();
        ApplyCmdVel();
    }

    private void UpdateCmdVelByKeyboard()
    {
        // Linear velocity
        if (Input.GetKeyDown(KeyCode.W))
            linearCmd.z += linearStep;

        if (Input.GetKeyDown(KeyCode.S))
            linearCmd.z -= linearStep;

        if (Input.GetKeyDown(KeyCode.D))
            linearCmd.x += linearStep;

        if (Input.GetKeyDown(KeyCode.A))
            linearCmd.x -= linearStep;

        if (Input.GetKeyDown(KeyCode.E))
            linearCmd.y += linearStep;

        if (Input.GetKeyDown(KeyCode.Q))
            linearCmd.y -= linearStep;

        // Angular velocity
        if (Input.GetKeyDown(KeyCode.RightArrow))
            angularCmd.y += yawStepDeg;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            angularCmd.y -= yawStepDeg;

        if (Input.GetKeyDown(KeyCode.UpArrow))
            angularCmd.x -= pitchStepDeg;

        if (Input.GetKeyDown(KeyCode.DownArrow))
            angularCmd.x += pitchStepDeg;

        // Stop
        if (Input.GetKeyDown(KeyCode.Space))
        {
            linearCmd = Vector3.zero;
            angularCmd = Vector3.zero;
        }
    }

    private void ClampCmdVel()
    {
        linearCmd.x = Mathf.Clamp(linearCmd.x, -maxLinearSpeed, maxLinearSpeed);
        linearCmd.y = Mathf.Clamp(linearCmd.y, -maxLinearSpeed, maxLinearSpeed);
        linearCmd.z = Mathf.Clamp(linearCmd.z, -maxLinearSpeed, maxLinearSpeed);

        angularCmd.x = Mathf.Clamp(angularCmd.x, -maxAngularSpeedDeg, maxAngularSpeedDeg);
        angularCmd.y = Mathf.Clamp(angularCmd.y, -maxAngularSpeedDeg, maxAngularSpeedDeg);
        angularCmd.z = Mathf.Clamp(angularCmd.z, -maxAngularSpeedDeg, maxAngularSpeedDeg);
    }

    private void ApplyCmdVel()
    {
        float dt = Time.deltaTime;

        Vector3 worldVelocity =
            transform.right * linearCmd.x +
            transform.up * linearCmd.y +
            transform.forward * linearCmd.z;

        transform.position += worldVelocity * dt;

        transform.Rotate(Vector3.up, angularCmd.y * dt, Space.World);
        transform.Rotate(Vector3.right, angularCmd.x * dt, Space.Self);
        transform.Rotate(Vector3.forward, angularCmd.z * dt, Space.Self);
    }
}