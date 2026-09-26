using UnityEngine;

namespace DynamicIslands
{
	//RTS Camera by FranzFischer78 

	public class RTSCamera : MonoBehaviour
	{
		// Speed at which the camera moves
		public float moveSpeed = 30.0f;

		// Speed at which the camera zooms
		public float zoomSpeed = 5.0f;

		// Minimum and maximum camera height
		public float minHeight = 1.0f;
		public float maxHeight = 150.0f;

		// Speed at which the camera rotates
		public float rotationSpeed = 100.0f;

		// Mouse rotation sensitivity
		public float mouseRotationSensitivity = 1.0f;


		// Reference to the parent game object for camera rotation
		public Transform cameraRotationParent;
		public Transform camTarget;

		/// <summary>World height of the sea of the island being edited.</summary>
		static float SeaY() { return (terraineditor.terrain != null ? terraineditor.terrain.transform.position.y : 0f) + DynamicIslands.EditorWaterLevel; }

		void Start()
		{
			cameraRotationParent = GameObject.Find("CamParent").transform;
			//camTarget = GameObject.Find("CamTarget").transform;
		}


		void Update()
		{
			// Don't fly around while typing an island name (WASD)
			if (global::DynamicIslands.Editor.EditorInput.IsTyping) return;

			// Get the horizontal and vertical input axis
			float horizontalInput = Input.GetAxis("Horizontal");
			float verticalInput = Input.GetAxis("Vertical");

			// Get the camera rotation input
			float rotationInput = Input.GetAxisRaw("CameraRotation");

			// Calculate the direction in which the camera should move
			Vector3 moveDirection = new Vector3(horizontalInput, 0, verticalInput);

			// Calculate the speed at which the camera should move based on its height (above or below the sea of the island being edited)
			float sea = SeaY();
			float speed = moveSpeed * Mathf.Clamp(Mathf.Abs(transform.position.y - sea), 6f, 300f) / 130f;

			// Move faster while holding the shift key
			if (Input.GetKey(KeyCode.LeftShift))
			{
				speed *= 2.0f;
			}

			// Move the camera in the specified direction
			transform.Translate(moveDirection * speed * Time.deltaTime);

			// Rotate the camera using the middle mouse button and mouse delta
			if (Input.GetMouseButton(1)) // 2 corresponds to the middle mouse button
			{
				//Debug.Log("try rotate");
				// Get the mouse delta
				float mouseDeltaX = Input.GetAxis("Mouse X");
				float mouseDeltaY = Input.GetAxis("Mouse Y");

				// Calculate the rotation based on the mouse delta and the sensitivity
				float rotationAmountX = mouseDeltaX * mouseRotationSensitivity;
				float rotationAmountY = mouseDeltaY * mouseRotationSensitivity;



				// Rotate the camera rotation parent around the world up vector
				transform.RotateAround(transform.position, Vector3.up, rotationAmountX);

				// Rotate the camera rotation parent around the world right vector
				transform.RotateAround(transform.position, Vector3.right, rotationAmountY);

				transform.SetPositionAndRotation(transform.position, Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y, 0f));
			}
		/*	else
			{
				// Calculate the rotation based on the input and the rotation speed
				float rotationAmount = rotationInput * rotationSpeed * Time.deltaTime;

				// Rotate the camera
				transform.Rotate(Vector3.up, rotationAmount);
			}*/

			// Zoom in or out with the scroll wheel
			// (not while the wheel scrolls a list in the editor's panels)
			bool overUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
			float scrollInput = overUI ? 0f : -Input.GetAxis("Mouse ScrollWheel");

			// Calculate the new camera height based on the scroll input
			float newHeight = transform.position.y + scrollInput * zoomSpeed;

			// Keep the camera between just above the terrain's base (under water, on a deep sea floor) and high above the sea
			float floor = terraineditor.terrain != null ? terraineditor.terrain.transform.position.y : 0f;
			newHeight = Mathf.Clamp(newHeight, floor + minHeight, sea + maxHeight + 250f);

			// Set the camera's position to the new height
			transform.position = new Vector3(transform.position.x, newHeight, transform.position.z);
		}
	}
}