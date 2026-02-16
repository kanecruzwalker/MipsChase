using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    // External tunables.
    static public float m_fMaxSpeed = 0.10f;
    public float m_fSlowSpeed = m_fMaxSpeed * 0.66f;
    public float m_fIncSpeed = 0.0025f;
    public float m_fMagnitudeFast = 0.6f;
    public float m_fMagnitudeSlow = 0.06f;
    public float m_fFastRotateSpeed = 0.2f;
    public float m_fFastRotateMax = 10.0f;
    public float m_fDiveTime = 0.3f;
    public float m_fDiveRecoveryTime = 0.5f;
    public float m_fDiveDistance = 3.0f;
    

    // Internal variables - track player's movement components each frame
    public Vector3 m_vDiveStartPos;
    public Vector3 m_vDiveEndPos;
    public float m_fAngle;
    public float m_fSpeed;
    public float m_fTargetSpeed;
    public float m_fTargetAngle;
    public eState m_nState;
    public float m_fDiveStartTime;


    /// <summary>   Player FSM states, cast to int for color array indexing.   </summary>
    public enum eState : int    // State Enumerators
    {
        kMoveSlow,
        kMoveFast,
        kDiving,
        kRecovering,
        kNumStates
    }


    private Color[] stateColors = new Color[(int)eState.kNumStates]
    {
        new Color(0,     0,   0),
        new Color(255, 255, 255),
        new Color(0,     0, 255),
        new Color(0,   255,   0),
    };


    /// <summary>   Returns true if player is diving. Used by Target.cs for catch detection.     /// </summary>
    /// <returns></returns>
    public bool IsDiving()      //For collision With MIPS
    {
        return (m_nState == eState.kDiving);
    }


    /// <summary>   Initiates a dive on left click if the player isn't already diving or recovering.
    ///             Calculates the dive trajectory from current position along the facing direction   /// </summary>
    void CheckForDive()     //Check if button click and dive state true 
    {
        if (Input.GetMouseButton(0) && (m_nState != eState.kDiving && m_nState != eState.kRecovering))
        {
            // Start the dive operation     //Transition to diving state
            m_nState = eState.kDiving;
            m_fSpeed = 0.0f;

            // Store starting parameters.   //Calculate dive trajectory from current position
            m_vDiveStartPos = transform.position;
            m_vDiveEndPos = m_vDiveStartPos - (transform.right * m_fDiveDistance);
            m_fDiveStartTime = Time.time;
        }
    }


    /// <summary>   Sets initial state: facing right, zero speed, slow movement mode.    /// </summary>
    void Start()
    {
        // Initialize variables.
        m_fAngle = 0;
        m_fSpeed = 0;
        m_nState = eState.kMoveSlow;
    }


    /// <summary>   Reads mouse position and calculates target direction and speed.
    ///             Mouse distance is normalized against screen size and mapped to three zones:
    ///             far(max speed), medium (slow speed), near (deadzone, zero speed).
    ///             /// </summary>
    void UpdateDirectionAndSpeed()
    {
        // Get relative positions between the mouse and player
        Vector3 vScreenPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 vScreenSize = Camera.main.ScreenToWorldPoint(new Vector2(Screen.width, Screen.height));
        Vector2 vOffset = new Vector2(transform.position.x - vScreenPos.x, transform.position.y - vScreenPos.y);

        // Find the target angle being requested.
        m_fTargetAngle = Mathf.Atan2(vOffset.y, vOffset.x) * Mathf.Rad2Deg;

        // Normalize mouse distance relative to screen diagonal.
        float fMouseMagnitude = vOffset.magnitude / vScreenSize.magnitude;

        // Map distance to target speed using threshold zones.
        if (fMouseMagnitude > m_fMagnitudeFast)
        {
            m_fTargetSpeed = m_fMaxSpeed;
        }
        else if (fMouseMagnitude > m_fMagnitudeSlow)
        {
            m_fTargetSpeed = m_fSlowSpeed;
        }
        else
        {
            m_fTargetSpeed = 0.0f;
        }
    }


    /// <summary>   Main physics loop. Processes input, executes the current state's behavior,
    ///             and updates the player's color to reflect the active state.               /// </summary>
    void FixedUpdate()
    {
        // Process input
        UpdateDirectionAndSpeed();
        CheckForDive();

        // Execute State Behavior
        switch (m_nState)
        {
            case eState.kMoveSlow:
                HandleMoveSlow();
                break;

            case eState.kMoveFast:
                HandleMoveFast();
                break;

            case eState.kDiving:
                HandleDiving();
                break;

            case eState.kRecovering:
                HandleRecovering();
                break;
        }
        
        // Update player's color to reflect current state
        GetComponent<Renderer>().material.color = stateColors[(int)m_nState];
    }




    /// <summary>
    /// SLOW MOVEMENT STATE
    /// 
    /// Behavior: 
    ///     - Player can rotate instantly to face the mouse direction
    ///     - Speed accelerates/decelerates toward the target speed
    ///     - Dive can be initiated from this state (handled by CheckForDive)
    ///     
    /// Transitions:
    ///     -> kMoveFast: When current speed > slow speed threshold
    ///     -> kDiving: When Left Mouse Button is pressed (handled by CheckForDive)
    /// </summary>
    void HandleMoveSlow ()
    {
        // State Check
        //Debug.Log("HandleMoveSlowEngaged");

        // Accelerate and decelerate toward target speed, clamped to prevent overshoot.
        if (m_fSpeed < m_fTargetSpeed)     
        {
            m_fSpeed = Mathf.Min(m_fSpeed + m_fIncSpeed, m_fTargetSpeed);
        }
        else
        {
            m_fSpeed = Mathf.Max(m_fSpeed - m_fIncSpeed, m_fTargetSpeed);
        }


        // In slow mode, rotation snaps instantly to mouse direction.
        m_fAngle = m_fTargetAngle;
        transform.rotation = Quaternion.Euler(0, 0, m_fAngle);


        // Apply movement along current facing direction.
        transform.position -= transform.right * m_fSpeed;


        // Transition to fast move once speed exceeds threshold
        if (m_fSpeed >= m_fSlowSpeed)
        {
            m_nState = eState.kMoveFast;
        }
    }



    /// <summary>
    /// 
    /// Wraps an angle difference to [-180, 180] so rotation takes the shortest path.
    /// 
    /// Ex: Current angle = 350, Target angle = 10
    ///     Without wrap the difference = -340 (nearly full rotation)
    ///     With wrapped difference = 20 (correct short arc)
    /// </summary>
    /// <param name="angle"> Raw angle difference in degrees </param>
    /// <returns> Equivalent angle in [-180, 180] range </returns>


    float WrapAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }



    /// <summary>
    /// FAST MOVEMENT STATE
    /// 
    /// Behavior:
    ///     - Player has momentum and cannot turn instantly.
    ///     - If mouse angle is within the fast rotate threshold, the player
    ///       gradually rotates toward it (interpolation via m_fFastRotateSpeed)
    ///     - If mouse angle is outside the threshold, the player maintains heading
    ///       but decelerates. Simulating overshooting / momentum
    ///       
    /// Transitions:
    ///     - kMoveSlow : When speed drops below the slow speed threshold
    /// 
    /// </summary>
    void HandleMoveFast()
    {
        // State Check
        //Debug.Log("MoveFast Engaged");

        // Calculate wrapped angle difference for shortest-path rotation
        float fAngleDifference = WrapAngle(m_fTargetAngle - m_fAngle);


        if(Mathf.Abs(fAngleDifference) <= m_fFastRotateMax)
        {
            // Within threshold : gradually rotate to target angle
            m_fAngle = Mathf.LerpAngle(m_fAngle, m_fTargetAngle, m_fFastRotateSpeed);
            transform.rotation = Quaternion.Euler(0, 0, m_fAngle);

            // Continue accelerating toward target speed 
            if(m_fSpeed < m_fTargetSpeed)
            {
                m_fSpeed = Mathf.Min(m_fSpeed + m_fIncSpeed, m_fTargetSpeed);
            }
        }
        else
        {
            // Outside threshold - can't turn, decelerate to simulate momentum
            m_fSpeed = Mathf.Max(m_fSpeed - m_fIncSpeed, 0.0f);
        }

        // Apply movement along current heading
        transform.position -= transform.right * m_fSpeed;

        // Transition back to slow when speed drops below threshold.
        if(m_fSpeed < m_fSlowSpeed)
        {
            m_nState = eState.kMoveSlow;
        }

    }


    /// <summary>
    /// DIVING STATE
    /// 
    /// Behavior:
    ///     - Player lunges forward from m_vDiveStartPos to m_vDiveEndPos
    ///     - Movement is a Lerp over m_fDiveTime seconds 
    ///     - No player input is processed during the dive
    ///     - This is the ONLY state where colliding with Mips triggers a catch
    ///     
    /// Transitions:
    ///     -> kRecovering: When dive timer expires (t >= 1.0)
    ///     
    /// Note: 
    ///     - The dive trajectory calculated in CheckForDive() using the player's
    ///       facing direction at the moment of initiation
    /// </summary>

    void HandleDiving()
    {
        // State Check
        //Debug.Log("Handle Dive Engaged");

        // Calculate normalized dive progress [0,1]
        float fElapsed = Time.time - m_fDiveStartTime;
        float t = fElapsed / m_fDiveTime;

        if (t < 1.0f)
        {
            // Dive in progress - interpolate position
            transform.position = Vector3.Lerp(m_vDiveStartPos, m_vDiveEndPos, t);
        }
        else
        {
            // Dive complete - snap to end position, reuse timer for recovery
            transform.position = m_vDiveEndPos;
            m_fDiveStartTime = Time.time;
            m_nState = eState.kRecovering;
        }
    }




    /// <summary>
    /// RECOVERY STATE
    /// 
    /// Behavior:
    ///     - Player cannot move. No input is accepted
    ///     - Lasts for m_fDiveRecoveryTime seconds
    ///     - Serves as a penalty/cooldown after diving, preventing dive spam
    ///     
    /// Transitions:
    ///     -> kMoveSlow: When recovery timer expires speed is reset to 0 so
    ///        player starts fresh in slow mode.
    /// </summary>
    void HandleRecovering()
    {
        // State Check
        //Debug.Log("Recovery Engaged");

        float fElapsed = Time.time - m_fDiveStartTime;

        if(fElapsed >= m_fDiveRecoveryTime)
        {
             
            // Recovery complete - reset speed and return to slow movement
            m_fSpeed = 0.0f;
            m_nState = eState.kMoveSlow;
        }
        // No movement during recovery
    }
}


 