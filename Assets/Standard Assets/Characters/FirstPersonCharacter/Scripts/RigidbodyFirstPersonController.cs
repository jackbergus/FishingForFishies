using System;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Characters.FirstPerson;
using System.Net;
using System.Net.Sockets;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Linq;

namespace UnityStandardAssets.Characters.FirstPerson
{

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class RigidbodyFirstPersonController : MonoBehaviour
    {
        // Moves the camera by just using the TCP messages
        private static bool useTcp = true;
        
        // Using the input keyboard/mouse method
        private static bool useInput = false;

        [Serializable]
        public class MovementSettings
        {
            public float ForwardSpeed = 8.0f;              // Speed when walking forward
            public float BackwardSpeed = 4.0f;             // Speed when walking backwards
            public float StrafeSpeed = 4.0f;               // Speed when walking sideways
            public float RunMultiplier = 2.0f;             // Speed when sprinting
            public KeyCode RunKey = KeyCode.LeftControl;   // Key for running (increase the spead)
            public float JumpForce = 30f;                  // Force associated to the jump
            public AnimationCurve SlopeCurveModifier = new AnimationCurve(new Keyframe(-90.0f, 1.0f), new Keyframe(0.0f, 1.0f), new Keyframe(90.0f, 0.0f));
            [HideInInspector] public float CurrentTargetSpeed = 8f;
            public MouseLook mouseLook = new MouseLook();  // Mouse orientation for rotation

#if !MOBILE_INPUT
            private bool m_Running;
#endif

            /*
	     * Some initialization, that is mainly performed only if there is the basic UNITY setting, with no TCP server
	     */
            public void Init(Transform transform, Transform cam)
            {
                mouseLook.Init(transform, cam);
            }

            public float x = 0.0f, y = 0.0f;
            public float xRot = 0.0f, yRot = 0.0f;
            public bool hitJump = false;
            public bool hitRun = false;


            public void Update()
            { 
	        /*
		 * Updating the Rover's setting via the keyboard/mouse if and only if 
		 * I enabled the reading from the input
		 */
                if (useInput)
                {
                    hitJump = CrossPlatformInputManager.GetButtonDown("Jump");
                    hitRun = Input.GetKey(RunKey);
                    xRot = CrossPlatformInputManager.GetAxis("Mouse X");
                    yRot = CrossPlatformInputManager.GetAxis("Mouse Y");
                }
            }

            public void FixedUpdate()
            {
                if (useInput)
                {
                    x = CrossPlatformInputManager.GetAxis("Horizontal");
                    y = CrossPlatformInputManager.GetAxis("Vertical");
                }
            }

            public Vector2 GetInput()
            {
                // Reading the values, either from the keyboard, or from the TCP packet
                Vector2 input = new Vector2
                (
                    x, y
                );
                UpdateDesiredTargetSpeed(input);
                return input;
            }

            public void UpdateDesiredTargetSpeed(Vector2 input)
            {
                if (input == Vector2.zero) return;
                if (input.x > 0 || input.x < 0)
                {
                    //strafe
                    CurrentTargetSpeed = StrafeSpeed;
                }
                if (input.y < 0)
                {
                    //backwards
                    CurrentTargetSpeed = BackwardSpeed;
                }
                if (input.y > 0)
                {
                    //forwards
                    //handled last as if strafing and moving forward at the same time forwards speed should take precedence
                    CurrentTargetSpeed = ForwardSpeed;
                }
#if !MOBILE_INPUT
                if (hitRun)
                {
                    CurrentTargetSpeed *= RunMultiplier;
                    m_Running = true;
                }
                else
                {
                    m_Running = false;
                }
#endif
            }

#if !MOBILE_INPUT
            public bool Running
            {
                get { return m_Running; }
            }

            internal void LookRotation(Transform transform1, Transform transform2)
            {
                ///Debug.Log(xRot);
                mouseLook.LookRotation(transform1, transform2, xRot, yRot);
            }
#endif
        }


        [Serializable]
        public class AdvancedSettings
        {
            public float groundCheckDistance = 0.01f; // distance for checking if the controller is grounded ( 0.01f seems to work best for this )
            public float stickToGroundHelperDistance = 0.5f; // stops the character
            public float slowDownRate = 20f; // rate at which the controller comes to a stop when there is no input
            public bool airControl; // can the user control the direction that is being moved in the air
            [Tooltip("set it to 0.1 or more if you get stuck in wall")]
            public float shellOffset; //reduce the radius by that ratio to avoid getting stuck in wall (a value of 0.1f is nice)
        }

        /*
	 * TCP communication utility class.
	 * I don't know which are the specs for the actual Rover, but I suppose that it would be
	 * better to have a UDP communication, as packet loss is not a major problem (take only the most recent one,
	 * and discard the others, by using Lamport's clocks).
	 */ 
        public class TCP
        {
	    // Buffer from which read the packages. Ideally, this should be the
	    // maximum size of a TCP packet, which is 65535. Ideally, we need to
	    // receive less information than that!
            public static int dataBufferSize = 4096;
	    
	    // IP associated to the ML server
            public string ip = "127.0.0.1";
	    // Port associated to the ML server
            public int port = 60260;
	    // Aforementioned Rover state update, where the movement information
	    // is going to be modified by the received TCP message
            MovementSettings settingsToMove;

            // Setting the Rover's information class
            public TCP(MovementSettings ms)
            {
                settingsToMove = ms;
            }

            public TcpClient socket;
            private NetworkStream stream = null;
            private byte[] receiveBuffer;

            /*
             * The TCP connection is good iff. I have a socket over which I can write a stream of data
             */
            public bool isTcpGood()
            {
                return (socket != null) && (stream != null);
            }

            public void Connect()
            {
                socket = new TcpClient
                {
                    ReceiveBufferSize = dataBufferSize,
                    SendBufferSize = dataBufferSize
                };
                receiveBuffer = new byte[dataBufferSize];
		// Establishing the initial handshake with the server
                socket.BeginConnect(ip, port, ConnectCallback, socket);
            }

            private void ConnectCallback(IAsyncResult _result)
            {
	        // On finish, everything is OK, closing the handshake procedure
                socket.EndConnect(_result);
                if (!socket.Connected) {
                    return;
                }
		// Establishing the stream (C# abstraction) from the socket (usual C representation)
                stream = socket.GetStream();
            }
	    
	    /*
	     * In this application, whenever we send the screen data, we expect to receive
	     * in return the actions that are required to atonomously move the Rover
	     */
            public void SendData(byte[] _packet)
            {
                try {
                    if (socket != null) {
		        // Sending the screenshot in jpg format
                        stream.BeginWrite(_packet, 0, _packet.Length, null, null);
			// Expecting to receive some data from the server
                        stream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
                    }
                } catch (Exception _ex) {
                    Debug.Log($"Error sending data to server via TCP: {_ex}");
                }
            }

            /*
	     * Asynchronous method that is invoked when we receive a result form the server
	     */
            private void ReceiveCallback(IAsyncResult _result)
            {
                Debug.Log("ReceiveCallback");
                try
                {
                    int _byteLength = stream.EndRead(_result);
                    if (_byteLength <= 0)
                    {
                        if (socket.Connected)
                            socket.Close();
                        // TODO: disconnect
                        return;
                    }

                    byte[] _data = new byte[_byteLength];
                    Array.Copy(receiveBuffer, _data, _byteLength);
                    HandleData(_data);
                    ///stream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
                }
                catch
                {
                    if (socket.Connected)
                        socket.Close();
                    // TODO: disconnect
                }
            }

            /*
	     * Returning the number of occurrences of character c in S
	     */
            private static float getNOccurences(string S, char c)
            {
                return S.Split(c).Length - 1;
            }

            /*
	     * Receiving the data from the server
	     */
            private bool HandleData(byte[] _data) {
                // Representing the received data as a single string
                var str = System.Text.Encoding.Default.GetString(_data);
                Debug.Log(str);
                int _packetLength = 0;
                settingsToMove.hitJump = false;
                settingsToMove.hitRun = false;
                settingsToMove.x = 0;
                settingsToMove.y = 0;
                settingsToMove.xRot = 0;
                settingsToMove.yRot = 0;

                // More efficient implementation: iteration over str[i] for each i from 0 to str.Length - 1
                if (str.Length > 0)
                {
                    // Jump
                    if (str.Contains("J"))
                        settingsToMove.hitJump = true;
                    // Run mode set: goes faster LRBF
                    if (str.Contains("T"))
                        settingsToMove.hitRun = true;
                    // Move left
                    settingsToMove.x += -1 * getNOccurences(str, 'L');
                    // Move right
                    settingsToMove.x += 1 * getNOccurences(str, 'R');
                    // Move backward
                    settingsToMove.y += -1 * getNOccurences(str, 'V');
                    // Move front
                    settingsToMove.y += 1 * getNOccurences(str, 'F');
                    // rotate left, deg
                    settingsToMove.xRot += -1 * getNOccurences(str, 'l');
                    // rotate right, deg
                    settingsToMove.xRot += 1 * getNOccurences(str, 'r');
                    // rotate up
                    settingsToMove.yRot += 2.0f * getNOccurences(str, 'U');
                    // rotate down
                    settingsToMove.yRot += -2.0f * getNOccurences(str, 'D');
                }

                return false;
            }
        }
        
        // Client-Server communication
        public TCP tcp;

        public Camera cam;
        public MovementSettings movementSettings = new MovementSettings();
        public AdvancedSettings advancedSettings = new AdvancedSettings();

        private Rigidbody m_RigidBody;
        private CapsuleCollider m_Capsule;
        private float m_YRotation;
        private Vector3 m_GroundContactNormal;
        private bool m_Jump;
        public static int resWidth = 2550;
        public static int resHeight = 3300;
        private int frameIndex = 0;
        GraphicsFormat format;
        /*
         * Number of seconds after which a new screenshot is sent to the multi-objective classifier
         */
        const float busyWaitMax = 1.0f;
        /*
         * Sending the screen as soon as possible
         */
        float busyWait = busyWaitMax;
       
        /*
         * I can snapshot the scene only after rendering it: so, I'm using LateUpdate
         */
        private void LateUpdate()
        {
            if ((tcp != null) && (tcp.isTcpGood()))
            {
                busyWait += Time.deltaTime;
                if (busyWait >= busyWaitMax) 
                {
                    busyWait = 0.0f; // I will need to wait more busyWaitMax for sending the next screenshot
                    RenderTexture rt = new RenderTexture(resWidth, resHeight, 24);
                    cam.targetTexture = rt;
                    Texture2D screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
                    cam.Render();
                    RenderTexture.active = rt;
                    screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
                    screenShot.Apply();
                    cam.targetTexture = null;

                    RenderTexture.active = null; // JC: added to avoid errors
                    Destroy(rt);                 // Memory free
                   
                    // In particular, this method will both send the JPG representation of the screen, and 
                    // receive the outcome of the communication with the server
                    tcp.SendData(screenShot.EncodeToJPG());
                }

            } else {
                busyWait = busyWaitMax;
            }

        }

        private void Start()
        {
            m_RigidBody = GetComponent<Rigidbody>();
            m_Capsule = GetComponent<CapsuleCollider>();
            if (useTcp)
            {
                // Setting the connection to the Server iff. this is up
                tcp = new TCP(movementSettings);
                // Establishing the connection
                tcp.Connect();
            }
            if (useInput)
            {
            else
            {
                ///m_IsGrounded = false;
                m_GroundContactNormal = Vector3.up;
            }
        }
    }
}
