using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>
    /// Free flying camera
    /// </summary>
    public class CameraFreeFly : MonoBehaviour
    {
        /// <summary>
        /// camera that will be moved by this object
        /// </summary>
        Camera MyCamera;

        /// <summary>
        /// Keys to move forward
        /// </summary>
        public List<KeyCode> Forward = new List<KeyCode>() { KeyCode.W, KeyCode.UpArrow };
        /// <summary>
        /// keys to move back
        /// </summary>
        public List<KeyCode> Back = new List<KeyCode>() { KeyCode.S, KeyCode.DownArrow };
        /// <summary>
        /// keys to move left
        /// </summary>
        public List<KeyCode> Left = new List<KeyCode>() { KeyCode.A, KeyCode.LeftArrow };
        /// <summary>
        /// keys to move right
        /// </summary>
        public List<KeyCode> Right = new List<KeyCode>() { KeyCode.D, KeyCode.RightArrow };
        /// <summary>
        /// keys to move up
        /// </summary>
        public List<KeyCode> up = new List<KeyCode>() { KeyCode.Space };
        /// <summary>
        /// keys to move down
        /// </summary>
        public List<KeyCode> down = new List<KeyCode>() { KeyCode.LeftControl };
        /// <summary>
        /// move faster key
        /// </summary>
        public List<KeyCode> sprint = new List<KeyCode>() { KeyCode.LeftShift };

        /// <summary>
        /// MoveSpeed of the camera
        /// </summary>
        public float MoveSpeed = 1.0f;
        /// <summary>
        /// Trun sensitivity of the camera
        /// </summary>
        public float Sensitivity = 1.0f;
        /// <summary>
        /// Hide player mouse?
        /// </summary>
        public bool HideMouse
        {
            get
            {
                return mousehide;
            }
            set
            {
                mousehide = value;

                if (mousehide)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Confined;
                    Cursor.visible = true;
                }
            }
        }
        bool mousehide = true;

        /// <summary>
        /// Only rotate when the input key is held down
        /// </summary>
        public KeyCode RotateWhenDown = KeyCode.None;

        /// <summary>
        /// Sprint speed multiplier
        /// </summary>
        public float SprintMultiplier = 1.4f;

        [HideInInspector]
        public float dx;
        [HideInInspector]
        public float dy;
        [HideInInspector]
        public Vector3 delta;

        private void Awake()
        {
            var rot_eul = transform.rotation.eulerAngles;
            this.dx = rot_eul.y;
            this.dy = rot_eul.x;
            MyCamera = GetComponent<Camera>();
        }

        public void Update()
        {
            if (MyCamera == null)
                throw new System.Exception("Error, FreeFlyCamera has a null attatched camera!");

            //get position of camera
            delta = Vector3.zero;
            var sprintm = 1.0f;

            //if ay keys in the forward list being pressed
            if (Forward != null && AnyGetKey(Forward))
            {
                delta += MyCamera.transform.forward;
            }
            if (Back != null && AnyGetKey(Back))
            {
                delta += -MyCamera.transform.forward;
            }
            if (Left != null && AnyGetKey(Left))
            {
                delta += -MyCamera.transform.right;
            }
            if (Right != null && AnyGetKey(Right))
            {
                delta += MyCamera.transform.right;
            }
            if (up != null && AnyGetKey(up))
            {
                delta += MyCamera.transform.up;
            }
            if (down != null && AnyGetKey(down))
            {
                delta += -MyCamera.transform.up;
            }
            if (sprint != null && AnyGetKey(sprint))
            {
                sprintm = SprintMultiplier;
            }

            delta = delta.normalized;
            delta *= MoveSpeed * sprintm * Time.deltaTime;

            transform.position += delta;

            //rotate camera based on mouse input
            if (RotateWhenDown == KeyCode.None || Input.GetKey(RotateWhenDown))
            {
                dx += Sensitivity * Input.GetAxis("Mouse X");
                dy -= Sensitivity * Input.GetAxis("Mouse Y");
                transform.rotation = Quaternion.Euler(new Vector3(dy, dx, 0));
            }
        }

        bool AnyGetKey(List<KeyCode> keys)
        {
            if (keys == null) return false;
            for (int i = 0; i < keys.Count; i++)
            {
                if (Input.GetKey(keys[i]))
                    return true;
            }
            return false;
        }
    }
}
