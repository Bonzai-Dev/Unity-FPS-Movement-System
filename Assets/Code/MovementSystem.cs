using Cinemachine;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class MovementSystem : MonoBehaviour
{
    private Vector2 MoveDirection;
    private float SprintValue;
    private Vector2 MouseDelta;
    private PlayerControls PlayerControls; 

    [Header("Camera Settings")]
    private float DefaultCameraFOV;
    [SerializeField] private CinemachineVirtualCamera PlayerCamera;
    [SerializeField] private Transform CameraTargetPosition;
    [SerializeField] private float CameraRunFOV = 20;
    [SerializeField] private float CameraBobbingSpeed = 1;
    [SerializeField] private float CameraBobbingDamping = 6;

    [Space(10)]
    [SerializeField] private float MouseSensitivity = 1;

    [Header("Movement Settings")]
    public TextMeshProUGUI Text;
    private GameObject Player;

    [Space(10)]
    [SerializeField] private ParticleSystem LandParticles;
    [SerializeField] private float JumpForce = 50;
    [SerializeField] private float CameraLandShakeIntensity = 0.2f;
    [SerializeField] private GameObject GroundCheck;

    [Space(10)]
    [SerializeField] private float WallCheckRadius = 0.6f;
    [SerializeField] private float WallClimbRadius = 0.6f;

    [Space(10)]
    [SerializeField] private float GroundCheckRadius = 0.15f;
    [SerializeField] private LayerMask GroundLayer;

    [Space(10)]
    [SerializeField] private Rigidbody PlayerRigidbody;
    [SerializeField] private float MovementSpeed = 4;
    [SerializeField] private float SprintSpeed = 2;
    [SerializeField] private float ClimbingSpeed = 7;

    [SerializeField] private float StartFriction = 40;
    [SerializeField] private float EndFriction = 10;

    private void Awake() {
        PlayerControls = new PlayerControls();

        PlayerControls.Player.Jump.performed += Jump;

        PlayerControls.Player.Sprint.performed += context => SprintValue = context.ReadValue<float>();
        PlayerControls.Player.Sprint.canceled += context => SprintValue = 0;

        PlayerControls.Player.Move.performed += context => MoveDirection = context.ReadValue<Vector2>();
        PlayerControls.Player.Move.canceled += context => MoveDirection = Vector2.zero;

        PlayerControls.Player.Look.performed += context => MouseDelta = context.ReadValue<Vector2>();
        PlayerControls.Player.Look.canceled += context => MouseDelta = Vector2.zero;
    }

    private void OnEnable() {
        PlayerControls.Enable();
    }

    private void OnDisable() {
        PlayerControls.Disable();
    }

    private void Start() {
        Player = this.gameObject;
        PlayerRigidbody.freezeRotation = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        DefaultCameraFOV = PlayerCamera.m_Lens.FieldOfView;
    }

    private void LateUpdate() {
        UpdateRotation();
    }


    private void Update() {
        CameraControl();
    }

    private void FixedUpdate() {
        MovePlayer();
        Text.text = PlayerRigidbody.velocity.ToString();
    }

    private float currentSprintSpeed;
    private float currentCameraSprintFOV;
    private Vector3 lastFallPosition;
    private bool intenseFall;
    private void MovePlayer() {
        // Running
        float cameraBobbingAmplitude = PlayerCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>().m_AmplitudeGain;
        if (CheckGround() && !CanClimb()) {
            currentCameraSprintFOV = Mathf.Lerp(currentCameraSprintFOV, CameraRunFOV * SprintValue * MoveDirection.sqrMagnitude, Time.fixedDeltaTime * CameraBobbingDamping);
            currentSprintSpeed = Mathf.Lerp(currentSprintSpeed, SprintSpeed, Time.fixedDeltaTime) * SprintValue;
            CameraBobbing(cameraBobbingAmplitude, cameraBobbingAmplitude, (CameraBobbingSpeed + currentSprintSpeed) * MoveDirection.sqrMagnitude);
        }
        else {
            currentCameraSprintFOV = Mathf.Lerp(currentCameraSprintFOV, 0, Time.fixedDeltaTime * CameraBobbingDamping / 2);
            CameraBobbing(cameraBobbingAmplitude, cameraBobbingAmplitude, 0);
        }

        PlayerCamera.m_Lens.FieldOfView = DefaultCameraFOV + currentCameraSprintFOV;

        // Camera landing shake
        float fallDistance = Vector3.Distance(new(0, PlayerRigidbody.velocity.y, 0), lastFallPosition);
        if (fallDistance >= 1) {
            intenseFall = true;
        }

        if (CheckGround() && intenseFall) {
            intenseFall = false;
                
            Debug.Log("Intense fall distance: " + fallDistance);

            if (fallDistance >= 10) {
                GameObject landParticles = Instantiate(LandParticles.gameObject, transform.position, Quaternion.identity);
                float duration = landParticles.GetComponent<ParticleSystem>().main.duration;
                float lifetime = landParticles.GetComponent<ParticleSystem>().main.startLifetime.constant;
                Destroy(landParticles, duration + lifetime);
            }

            CameraBobbing(
                cameraBobbingAmplitude, 
                Mathf.Clamp(cameraBobbingAmplitude, CameraLandShakeIntensity * fallDistance, CameraLandShakeIntensity), 
                Time.fixedDeltaTime * 5
            );
        }
            
        lastFallPosition = new(0, PlayerRigidbody.velocity.y, 0);

        // Moving
        if (MoveDirection != Vector2.zero) {
            Vector3 playerMovementVelocity = Vector3.Normalize(transform.right * MoveDirection.x + transform.forward * MoveDirection.y);
            if (CanClimb()) {
                playerMovementVelocity += Vector3.Normalize(transform.up * MoveDirection.y);
                PlayerRigidbody.velocity = new(PlayerRigidbody.velocity.x, 0, PlayerRigidbody.velocity.z);
            }

            Vector3 lerpVector = new(playerMovementVelocity.x * MovementSpeed, PlayerRigidbody.velocity.y + playerMovementVelocity.y * ClimbingSpeed, playerMovementVelocity.z * MovementSpeed);
            PlayerRigidbody.velocity = Vector3.Lerp(PlayerRigidbody.velocity, lerpVector, Time.fixedDeltaTime * StartFriction);
        }

        else if (MoveDirection == Vector2.zero) {
            PlayerRigidbody.velocity = Vector3.Lerp(PlayerRigidbody.velocity, new Vector3(0, PlayerRigidbody.velocity.y), Time.fixedDeltaTime * EndFriction);
        }
    }

    private void Jump(InputAction.CallbackContext context) {
        if (CheckGround())
            PlayerRigidbody.AddForce(new(MoveDirection.x * JumpForce * MovementSpeed, JumpForce, MoveDirection.y * JumpForce * MovementSpeed), ForceMode.Impulse);
    }

    private bool CheckGround() {
        return Physics.CheckSphere(GroundCheck.transform.position, GroundCheckRadius, GroundLayer) && !(Mathf.Abs(PlayerRigidbody.velocity.y) >= 3);
    }

    private bool CanClimb() {
        return Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, WallClimbRadius) && hit.transform.TryGetComponent(out Wall wall) && wall.Climbable;
    }

    private Collider[] TouchingColliders() {
        Vector3 point1 = transform.position + new Vector3(0, 0.5f, 0);
        Vector3 point2 = transform.position + new Vector3(0, 1.5f, 0);
        return Physics.OverlapCapsule(point1, point2, WallCheckRadius);
    }

    private float xRotation;
    private float yRotation;
    private void CameraControl() {
        float cameraSensitivity = MouseSensitivity * 50;

        yRotation += MouseDelta.x * Time.deltaTime * cameraSensitivity;
        xRotation -= MouseDelta.y * Time.deltaTime * cameraSensitivity;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
    }

    private void UpdateRotation() {
        PlayerCamera.transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
        Player.transform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    private void CameraBobbing(float cameraBobbingAmplitude, float lerpFrom, float lerpTo) {
        PlayerCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>().m_AmplitudeGain = Mathf.Lerp(
            lerpFrom,
            lerpTo,
            Time.fixedDeltaTime *
            CameraBobbingDamping
        );
    }

    private void OnDrawGizmos() {
        // Draws gizmos for ground check
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GroundCheck.transform.position, GroundCheckRadius);
    }
}
