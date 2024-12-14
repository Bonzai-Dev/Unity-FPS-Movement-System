using Cinemachine;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class MovementSystem : MonoBehaviour
{
    private Vector2 MoveDirection;
    private float SprintValue;
    private float CrouchValue;
    private Vector2 MouseDelta;
    private PlayerControls PlayerControls; 

    [Header("Camera Settings")]
    private float DefaultCameraFOV;
    [SerializeField] private CinemachineVirtualCamera PlayerCamera;
    [SerializeField] private float CameraRunFOV = 20;
    [SerializeField] private float CameraBobbingSpeed = 1;
    [SerializeField] private float CameraBobbingDamping = 6;

    [Space(10)]
    [SerializeField] private float MouseSensitivity = 0.5f;

    [Header("Movement Settings")]
    public TextMeshProUGUI Text;
    public TextMeshProUGUI FPS;
    private GameObject Player;

    [Space(10)]
    [SerializeField] private ParticleSystem LandParticles;
    [SerializeField] private float JumpForce = 50;
    [SerializeField] private float CameraLandShakeIntensity = 0.2f;
    [SerializeField] private float IntenseFallDistance = 6;
    [SerializeField] private GameObject GroundCheck;

    [Space(10)]
    [SerializeField] private float WallCheckRadius = 0.6f;
    [SerializeField] private float WallClimbRadius = 0.6f;

    [Space(10)]
    [SerializeField] private float GroundCheckRadius = 0.5f;
    [SerializeField] private LayerMask GroundLayer;

    [Space(10)]
    [SerializeField] private Rigidbody PlayerRigidbody;
    [SerializeField] private float CrouchSize = 3;
    [SerializeField] private float MovementSpeed = 4;
    [SerializeField] private float CrouchSpeed = 2;
    [SerializeField] private float SprintSpeed = 2;
    [SerializeField] private float ClimbingSpeed = 7;

    [Space(10)]
    [SerializeField] private float StartFriction = 40;
    [SerializeField] private float EndFriction = 10;
    [SerializeField] private float MaxSlopeAngle = 50;

    private void Awake() {
        PlayerControls = new PlayerControls();

        PlayerControls.Player.Jump.performed += Jump;

        PlayerControls.Player.Sprint.performed += context => SprintValue = context.ReadValue<float>();
        PlayerControls.Player.Sprint.canceled += context => SprintValue = 0;

        PlayerControls.Player.Crouch.performed += context => CrouchValue = context.ReadValue<float>();
        PlayerControls.Player.Crouch.canceled += context => CrouchValue = 0;

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

    private float xRotation;
    private float yRotation;
    private void LateUpdate() {
        PlayerCamera.transform.rotation = Quaternion.Euler(xRotation, yRotation, 0);
        PlayerRigidbody.transform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    private void Update() {
        float cameraSensitivity = MouseSensitivity * 50;
        yRotation += MouseDelta.x * Time.deltaTime * cameraSensitivity;
        xRotation -= MouseDelta.y * Time.deltaTime * cameraSensitivity;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        int fps = Mathf.RoundToInt(1 / Time.deltaTime);
        FPS.text = fps.ToString();
    }

    private void FixedUpdate() {
        MovePlayer();
        Text.text = PlayerRigidbody.velocity.ToString();
    }

    private float currentSprintSpeed;
    private float currentCameraSprintFOV;
    
    private Vector3 lastFallPosition;
    private bool intenseFall;
    
    private Vector3 slopeNormal;
    private void MovePlayer() {
        // Crouching   
        if (IsCrouching())
            transform.localScale = new Vector3(1, 1 / CrouchSize, 1);
        else if (!Physics.Raycast(transform.position, transform.up, 2.2f, ~(1 << gameObject.layer)))
            transform.localScale = new Vector3(1, 1, 1);

        // Running
        float cameraBobbingAmplitude = PlayerCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>().m_AmplitudeGain;
        if (!CanClimb() && CheckGround()) {
            int crouch = !IsCrouching() ? 1 : 0;
            float speedToLerp = SprintValue * MoveDirection.sqrMagnitude * crouch;
            currentSprintSpeed = Mathf.Lerp(currentSprintSpeed, SprintSpeed * speedToLerp, Time.fixedDeltaTime * StartFriction);
            currentCameraSprintFOV = Mathf.Lerp(currentCameraSprintFOV, CameraRunFOV * speedToLerp, Time.fixedDeltaTime * CameraBobbingDamping);
            CameraBobbing(cameraBobbingAmplitude, (CameraBobbingSpeed + currentSprintSpeed) * MoveDirection.sqrMagnitude);
        }
        else {
            currentSprintSpeed = Mathf.Lerp(currentSprintSpeed, 0, Time.fixedDeltaTime);
            currentCameraSprintFOV = Mathf.Lerp(currentCameraSprintFOV, 0, Time.fixedDeltaTime * CameraBobbingDamping / 2);
            CameraBobbing(cameraBobbingAmplitude, 0);
        }

        PlayerCamera.m_Lens.FieldOfView = DefaultCameraFOV + currentCameraSprintFOV;

        // Camera landing shake
        float fallDistance = Vector3.Distance(new(0, PlayerRigidbody.velocity.y, 0), lastFallPosition);
        if (fallDistance >= IntenseFallDistance)
            intenseFall = true;

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
                Mathf.Clamp(cameraBobbingAmplitude, CameraLandShakeIntensity * fallDistance, CameraLandShakeIntensity), 
                Time.fixedDeltaTime * 5
            );
        }
            
        lastFallPosition = new(0, PlayerRigidbody.velocity.y, 0);

        // Moving
        if (MoveDirection != Vector2.zero) {
            Vector3 playerMovementVelocity = Vector3.Normalize(transform.right * MoveDirection.x + transform.forward * MoveDirection.y);

            if (OnSlope()) {
                playerMovementVelocity = Vector3.ProjectOnPlane(playerMovementVelocity, slopeNormal).normalized;
                playerMovementVelocity.y = 0;
            }

            else if (CanClimb()) {
                playerMovementVelocity += Vector3.Normalize(transform.up * MoveDirection.y);
                PlayerRigidbody.velocity = new(
                    PlayerRigidbody.velocity.x, 
                    playerMovementVelocity.y * ClimbingSpeed - (CrouchValue * ClimbingSpeed / CrouchSize), 
                    PlayerRigidbody.velocity.z
                );
            }

            float moveSpeed = MovementSpeed + currentSprintSpeed - CrouchSpeed * CrouchValue;
            Vector3 lerpVector = new(
                playerMovementVelocity.x * moveSpeed, 
                PlayerRigidbody.velocity.y, 
                playerMovementVelocity.z * moveSpeed
            );
            
            PlayerRigidbody.velocity = Vector3.Lerp(PlayerRigidbody.velocity, lerpVector, Time.fixedDeltaTime * StartFriction);
        }

        else if (MoveDirection == Vector2.zero) {
            float lerpY = PlayerRigidbody.velocity.y;
            if (OnSlope()) 
                lerpY = 0;

            PlayerRigidbody.velocity = Vector3.Lerp(PlayerRigidbody.velocity, new Vector3(0, lerpY), Time.fixedDeltaTime * EndFriction);
        }
     
        PlayerRigidbody.useGravity = !OnSlope();
    }

    private bool IsCrouching() {
        return CrouchValue != 0;
    }

    private void Jump(InputAction.CallbackContext context) {
        float moveSpeed = MovementSpeed + currentSprintSpeed;
        Vector3 jumpDirection = MoveDirection.x * moveSpeed * transform.right + MoveDirection.y * moveSpeed * transform.forward;
        Vector3 jumpHeight = (JumpForce - CrouchValue * JumpForce / CrouchSize) * transform.up;
        if (CheckGround())
            PlayerRigidbody.AddForce(
                jumpDirection + jumpHeight,
                ForceMode.Impulse
            );
    }

    private bool CheckGround() {
        return Physics.CheckSphere(GroundCheck.transform.position, GroundCheckRadius, GroundLayer) && !(Mathf.Abs(PlayerRigidbody.velocity.y) >= 3);
    }

    private bool CanClimb() {
        return Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, WallClimbRadius) && hit.transform.TryGetComponent(out Wall wall) && wall.Climbable;
    }

    private bool OnSlope() {
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit slopeHit, 0.1f)) {
            slopeNormal = slopeHit.normal;
            float angle = Vector3.Angle(Vector3.up, slopeNormal);
            return angle < MaxSlopeAngle && angle != 0;
        }

        return false;
    }

    private Collider[] TouchingColliders() {
        Vector3 point1 = transform.position + new Vector3(0, 0.5f, 0);
        Vector3 point2 = transform.position + new Vector3(0, 1.5f, 0);
        return Physics.OverlapCapsule(point1, point2, WallCheckRadius);
    }

    private void CameraBobbing(float lerpFrom, float lerpTo) {
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
