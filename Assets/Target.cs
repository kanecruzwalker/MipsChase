using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Target : MonoBehaviour
{
    // References
    // Check player position for proximity detection
    // Check IsDiving() state for catch determination
    // Acquired automatically in Start() via FindObjectOfType

    public Player m_player;

    // STATE ENUMERATION FSM STATES FOR TARGET
    public enum eState : int
    {
        kIdle,          // 0 - Stationary, waiting
        kHopStart,      // 1 - Calculating hop direction
        kHop,           // 2 - Executing hop movement
        kCaught,        // 3 - Attached to player, game over
        kTaunt,         // 4 - Taunting the player during recovery
        kNumStates      // 5 - Sentinel value (array size helper)
    }


    // STATE COLOR MAPPING
    private Color[] stateColors = new Color[(int)eState.kNumStates]
   {
        new Color(255, 0,   0),
        new Color(0,   255, 0),
        new Color(0,   0,   255),
        new Color(255, 255, 255),
        new Color(255, 255, 0)      // kTaunt Yellow
   };

    // External tunables.
    public float m_fHopTime = 0.2f;         // Duration of each hop in seconds
    public float m_fHopSpeed = 6.5f;        // Distance of each hop in world units
    public float m_fScaredDistance = 3.0f;  // Detection radius
    public int m_nMaxMoveAttempts = 50;     // Max random direction attempts when calculating a hop
    public float m_fTauntTime = 0.5f;       // Duration of the taunt spin
    public AudioClip m_tauntClip;           // Taunt sound effect (assign in Inspector)

    // Internal variables.
    public eState m_nState;          // Current FSM state
    public float m_fHopStart;        // Timestamp when the current hop began
    public Vector3 m_vHopStartPos;   // World position where the current hop started
    public Vector3 m_vHopEndPos;     // World position where the current hop will end
    public float m_fTauntStartTime;  // Timestamp when taunt began
    public float m_fTauntStartAngle; // Starting angle for the spin
    public AudioSource m_audioSource;// Audio source component


    /// <summary>
    /// 
    /// Called once on scene load. Sets initial state to Idle and 
    /// acquires a reference to the Player component in the scene.
    /// 
    /// Note: FindObjectOfType is used instead of direct Inspector assignment
    /// </summary>
    void Start()
    {
        // Setup the initial state and get the player GO.
        m_audioSource = GetComponent<AudioSource>();
        m_nState = eState.kIdle;
        m_player = GameObject.FindObjectOfType(typeof(Player)) as Player;
    }



    // UTILITY METHODS 
    /// <summary>
    /// Calculates the visible screen boundaries in world coordinates.
    /// Used to ensure MIPS doesn't hop off-screen
    /// 
    /// Returns a Vector2 representing half the screen size in world units. 
    /// The full visible area spans from -bounds to +bounds on each axis
    /// 
    /// A small margin is subtracted to keep MIPS visually on-screen
    /// (not partially clipped at the edge)
    /// </summary>
    /// <returns>Half-extents of the visible screen area in world space.</returns>
    
    Vector2 GetScreenBounds()
    {
        // Convert top-right corner of screen from viewport to world space.
        // ViewportToWorldPoint(1,1) = top-right corner.
        Vector3 screenTopRight = Camera.main.ViewportToWorldPoint(new Vector3(1, 1, 0));

        // Subtract a small margin (0.5 units) to Mips stays visually on-screen.
        return new Vector2(
            Mathf.Abs(screenTopRight.x) - 0.5f,
            Mathf.Abs(screenTopRight.y) - 0.5f
        );
    }



    /// <summary>
    /// Checks whether a given world position is within the visible screen bounds.
    /// </summary>
    /// <param name="pos">Position to check.</param>
    /// <returns>True if the position is within screen bounds</returns>
    bool IsWithinBounds(Vector3 pos)
    {
        Vector2 bounds = GetScreenBounds();
        return Mathf.Abs(pos.x) < bounds.x && Mathf.Abs(pos.y) < bounds.y;
    }


    /// <summary>
    /// Clamps a position to stay within screen bounds.
    /// Used as a fallback when no valid hop direction can be found.
    /// </summary>
    /// <param name="pos">Position to clamp</param>
    /// <returns>Clamped position within screen bounds</returns>
    Vector3 ClampToBounds(Vector3 pos)
    {
        Vector2 bounds = GetScreenBounds();
        pos.x = Mathf.Clamp(pos.x, -bounds.x, bounds.x);
        pos.y = Mathf.Clamp(pos.y, -bounds.y, bounds.y);
        return pos;
    }




    // CORE FSM UPDATE
    /// <summary>
    /// Physics update loop. Handles FSM state execution and visual feedback.
    /// 
    /// We use FixedUpdate for the same reasons as Player.cs:
    /// framerate-independent physics timing and consistency with the 
    /// template's existing color update.
    /// </summary>
    void FixedUpdate()
    {
        // Execute behavior for the current state.
        switch (m_nState)
        {
            case eState.kIdle:
                HandleIdle();
                break;

            case eState.kHopStart:
                HandleHopStart();
                break;

            case eState.kHop:
                HandleHop();
                break;

            case eState.kCaught:
                // No behavior needed - Mips is parented to the player
                // and moves with them automatically (set in OnTriggerStay2D)
                break;

            case eState.kTaunt:
                HandleTaunt();
                break;
        }
        
        // Update visual state feedback
        GetComponent<Renderer>().material.color = stateColors[(int)m_nState];
    }




    // STATE HANDLERS
    /// <summary>
    /// IDLE STATE
    /// 
    /// Behavior: 
    ///     - Mips stays completely still
    ///     - Continuously checks the distance to the player
    ///     
    /// Transitions:
    ///     -> kHopStart: When player enters the scared distance radius
    /// </summary>
    
    void HandleIdle()
    {
        // Calculate distance to player
        float fDistance = Vector3.Distance(transform.position, m_player.transform.position);

        // If player is within the scared radius, start evading
        if(fDistance < m_fScaredDistance)
        {
            m_nState = eState.kHopStart;
        }
    }





    /// <summary>
    /// HOP START STATE (Hop Direction Calculation)
    /// 
    /// Behavior:
    ///     This is a "thinking" state where Mips calculates where to hop.
    ///     The evasion algorithm uses a multi-strategy approach
    ///     
    ///     Strategy 1 - Direct Escape:
    ///         Calculate the vector directly away from the player and attempt to hop in that direction.
    ///         If the destination is on-screen, use it.
    ///         
    ///     Strategy 2 - Random Scatter:
    ///         If direct escape leads off-screen, try up to m_nMaxMoveAttempts random angles.
    ///         For each, check if the resulting position is:
    ///             a) Within screen bounds
    ///             b) Farther from the player than the current position
    ///         Accept the first valid direction found
    ///         
    ///     Strategy 3 - Fallback (Cornered):
    ///         If no valid direction is found, clamp the direct escape position to screen bounds.
    ///         Mips may end up close to the player but won't leave the screen.
    ///         
    /// Transitions:
    ///     -> kHop: Immediately after calculating the hop destination.
    ///     
    /// This state typically lasts only a single frame (calculation + transition)
    /// </summary>

    void HandleHopStart()
    {
        // Strategy 1: Direct escape from Player
        // Calculate the vector pointing directly away from the player
        Vector3 vAwayFromPlayer = (transform.position - m_player.transform.position).normalized;


        // Calculate candidate hop destination
        Vector3 vCandidate = transform.position + vAwayFromPlayer * m_fHopSpeed;


        // Check if direct escape stays on-screen
        if (IsWithinBounds(vCandidate))
        {
            // Direct escape is valid - use it
            m_vHopEndPos = vCandidate;
        }
        else
        {
            // Strategy 2: Random angle search
            // Direct path leads off-screen. Try random angle to find 
            // an on-screen destination that moves away from the player.
            bool bFoundValid = false;
            float fCurrentDistance = Vector3.Distance(
                    transform.position,
                    m_player.transform.position
             );


            for (int i = 0; i < m_nMaxMoveAttempts; i++)
            {
                // Generate a random direction.
                float fRandomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 vRandomDir = new Vector3(
                    Mathf.Cos(fRandomAngle),
                    Mathf.Sin(fRandomAngle),
                    0
                );

                Vector3 vTestPos = transform.position + vRandomDir * m_fHopSpeed;

                // Validate: on-screen AND farther from player than current pos.
                if (IsWithinBounds(vTestPos))
                {
                    float fNewDistance = Vector3.Distance(
                        vTestPos, m_player.transform.position);

                    if(fNewDistance > fCurrentDistance)
                    {
                        m_vHopEndPos = vTestPos;
                        bFoundValid = true;
                        break;
                    }
                }
            }


            // Strategy 3: Fallback - clamp to bounds
            if (!bFoundValid)
            {
                // Cornered - clamp the direct escape to screen edges.
                // Mips stays on-screen even if it can't get farther away
                m_vHopEndPos = ClampToBounds(vCandidate);
            }
        }

        // Rotate Mips to face the hop direction.
        Vector3 vHopDir = (m_vHopEndPos - transform.position).normalized;
        float fHopAngle = Mathf.Atan2(vHopDir.y, vHopDir.x) * Mathf.Rad2Deg + 270f;
        transform.rotation = Quaternion.Euler(0, 0, fHopAngle);

        // Record hop starting parameters and transition to hop execution.
        m_vHopStartPos = transform.position;
        m_fHopStart = Time.time;
        m_nState = eState.kHop;
    }




    /// <summary>
    /// HOP STATE (Movement Execution)
    /// 
    /// Behavior: 
    ///     - Mips lerps from m_vHopStartPos to m_vHopEndPos over m_fHopTime
    ///     - The hop is visually distinct - a quick snap movement
    ///     - No input or decisions are made during the hop itself
    ///     
    /// Transitions:
    ///     -> kIdle: If hop completes and player is far away (safe)
    ///     -> kHopStart: If hop completes but player is still within range
    ///                   This creates a chain of hops for persistent pursuit.
    /// </summary>
    

    void HandleHop()
    {
        // Calculate normalized hop progress [0,1].
        float fElapsed = Time.time - m_fHopStart;
        float t = fElapsed / m_fHopTime;

        if(t < 1.0f)
        {
            // Hop in progress - interpolate position
            transform.position = Vector3.Lerp(m_vHopStartPos, m_vHopEndPos, t);
        }
        else
        {
            // Hop complete - snap to end position
            transform.position = m_vHopEndPos;

            //If player is recovering from a missed dive, taunt them
            if(m_player.m_nState == Player.eState.kRecovering)
            {
                m_fTauntStartTime = Time.time;
                m_fTauntStartAngle = transform.rotation.eulerAngles.z;
                m_nState = eState.kTaunt;

                //Play taunt sound if available
                if(m_audioSource != null && m_tauntClip != null)
                {
                    m_audioSource.PlayOneShot(m_tauntClip);
                }
                return; // Skip the normal proximity check
            }

            // Decide next state based on player proximity
            float fDistance = Vector3.Distance(
                   transform.position,
                   m_player.transform.position
            );

            if(fDistance < m_fScaredDistance)
            {
                // Player is still close - chain another hop immediately
                m_nState = eState.kHopStart;
            }
            else
            {
                // Player is far enough - return to idle.
                m_nState = eState.kIdle;
            }
        }
    }



    ///<summary>
    /// TAUNT STATEd
    /// 
    /// Behavior: 
    ///     - Mips does a 360 spin and plays a tuant sound.
    ///     - Triggered when a hop completes while the player is recovering.
    ///     
    /// Transitions:
    ///     -> kIdle: If spin completes and player is far away
    ///     -> kHopStart: If spin completes and player is till close
    /// </summary>
    
    void HandleTaunt()
    {
        float fElapsed = Time.time - m_fTauntStartTime;
        float t = fElapsed / m_fTauntTime;

        if (t < 1.0f)
        {
            // Spin 360 degrees over the taunt duration
            float fCurrentAngle = m_fTauntStartAngle + (360f * t);
            transform.rotation = Quaternion.Euler(0, 0, fCurrentAngle);
        }
        else
        {
            // Taunt complete - check proximity for next state
            float fDistance = Vector3.Distance(transform.position, m_player.transform.position);

            if(fDistance < m_fScaredDistance)
            {
                m_nState = eState.kHopStart;
            }
            else
            {
                m_nState = eState.kIdle;
            }
        }
    }







    //  COLLISION DETECTION 
    /// <summary>
    /// 
    /// Called by Unity's 2D physics when another collider stays within an objects trigger zone
    /// 
    /// Catch Logic:
    ///     - Check if the colliding object is the Player
    ///     - If the player is currently in the Diving state (IsDiving()),
    ///     Mips is caught:
    ///         1. State transitions to kCaught
    ///         2. Mips is parented to the Player transform
    ///         3. Mips is positioned below the player (-.5 on Y axis).
    ///     This makes Mips visually "held" by the player and move with them.
    ///     
    /// Note: OnTriggerStay2D fires every physics frame while overlapping, not just
    /// on initial contact. This ensures the catch registers even if the dive begins after overlap starts
    /// 
    /// The player must be DIVING for a catch. Simply walking over Mips does nothing
    /// 
    /// </summary>
    void OnTriggerStay2D(Collider2D collision)
    {
        // Check if this is the player (in this situation it should be!)
        if (collision.gameObject == GameObject.Find("Player"))
        {
            // If the player is diving, it's a catch!
            if (m_player.IsDiving())
            {
                m_nState = eState.kCaught;
                transform.parent = m_player.transform;
                transform.localPosition = new Vector3(0.0f, -0.5f, 0.0f);
                transform.rotation = m_player.transform.rotation * Quaternion.Euler(0,0,90); // Face same direction as player
            }
        }
    }
}