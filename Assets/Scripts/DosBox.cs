﻿using System;
using UnityEngine;
using System.Linq;
using System.Diagnostics;

public class DosBox : MonoBehaviour
{
	public GUIText RightText;
	public GameObject Actors;
	public GameObject Arrow;
	public Box BoxPrefab;

	//initial player position
	private int dosBoxPattern;
	private byte[][] PlayerInitialPosition = new byte[][]
	{
		new byte[] { 0x9F, 0x0C, 0x00, 0x00, 0xF4, 0xF9, 0x9F, 0x0C, 0x00, 0x00, 0xF4, 0xF9 }, //AITD1
		new byte[] { 0x43, 0x01, 0x00, 0x00, 0xD0, 0xE4, 0x43, 0x01, 0x00, 0x00, 0xD0, 0xE4 }, //AIID2
		new byte[] { 0x3F, 0x03, 0x00, 0x00, 0x00, 0x00, 0x3F, 0x03, 0x00, 0x00, 0x00, 0x00 }, //AITD3
		new byte[] { 0x27, 0x03, 0x00, 0x00, 0x00, 0x00, 0x27, 0x03, 0x00, 0x00, 0x00, 0x00 }  //AITD3 (bis)
	};

	private int[] ActorStructSize = new int[] { 160, 180, 182, 182 };
	private int[] MemoryOffsets = new int[] { -28 - 160, -28, -28, -28 };
	private int[] PlayerActorIndex = new int[] { 1, 1, 2, 2 };

	private string[] cardinalPositions = new [] { "N", "E", "S", "W" };

	private Vector3 lastPlayerPosition;
	private int linkfloor = 0;
	private int linkroom = 0;
	private ProcessHelper memoryProcess;
	private long memoryAddress;
	private byte[] memory;


	public void Start()
	{
		//game has maximum 50 actors
		for (int i = 0; i < 50; i++)
		{
			Box box = Instantiate(BoxPrefab);
			box.transform.parent = Actors.transform;
			box.name = "Actor";
		}
	}

	public void Update()
	{
		GameObject player = null;
		int playerActorIndex = PlayerActorIndex[dosBoxPattern];

		if (memoryProcess != null)
		{
			if (memoryProcess.Read(memory, memoryAddress + MemoryOffsets[dosBoxPattern], memory.Length) > 0)
			{
				int i = 0;
				foreach (Box box in Actors.GetComponentsInChildren<Box>(true))
				{
					int k = i * ActorStructSize[dosBoxPattern];
					int floorNumber = ReadShort(memory[k + 46], memory[k + 47]);
					int roomNumber = ReadShort(memory[k + 48], memory[k + 49]);

					int objectid = ReadShort(memory[k + 0], memory[k + 1]);
					int body = ReadShort(memory[k + 2], memory[k + 3]);
					bool isActive = objectid != -1;

					//player
					if (isActive && objectid == playerActorIndex)
					{
						//automatically switch room and floor (has to be done before setting other actors positions)
						if (linkfloor != floorNumber || linkroom != roomNumber)
						{
							linkfloor = floorNumber;
							linkroom = roomNumber;

							GetComponent<RoomLoader>().RefreshRooms(linkfloor, linkroom);
						}
					}

					Transform roomObject = GetComponent<RoomLoader>().GetRoom(floorNumber, roomNumber);
					if (isActive && roomObject != null)
					{
						//local position
						int w = k + 8;
						int x = (ReadShort(memory[w + 0], memory[w + 1]) + ReadShort(memory[w + 2], memory[w + 3])) / 2;
						int y = (ReadShort(memory[w + 4], memory[w + 5]) + ReadShort(memory[w + 6], memory[w + 7])) / 2;
						int z = (ReadShort(memory[w + 8], memory[w + 9]) + ReadShort(memory[w + 10], memory[w + 11])) / 2;

						//local to global position
						x += (int)(roomObject.localPosition.x * 1000.0f);
						y += (int)(roomObject.localPosition.y * 1000.0f);
						z += (int)(roomObject.localPosition.z * 1000.0f);

						box.transform.position = new Vector3(x, -y, z) / 1000.0f;

						//make actors appears slightly bigger than they are to be not covered by actors
						float delta = 1.0f;
						box.transform.localScale = new Vector3(
							ReadShort(memory[w + 2], memory[w + 3]) - ReadShort(memory[w + 0], memory[w + 1]) + delta,
							ReadShort(memory[w + 6], memory[w + 7]) - ReadShort(memory[w + 4], memory[w + 5]) + delta,
							ReadShort(memory[w + 10], memory[w + 11]) - ReadShort(memory[w + 8], memory[w + 9]) + delta) / 1000.0f;

						box.ID = objectid;
						box.Body = body;
						box.Room = roomNumber;
						box.Flags = ReadShort(memory[k + 4], memory[k + 5]);
						box.Life = ReadShort(memory[k + 52], memory[k + 53]);
						box.Anim = ReadShort(memory[k + 62], memory[k + 63]);
						box.Frame = ReadShort(memory[k + 74], memory[k + 75]);
						box.Speed = ReadShort(memory[k + 116], memory[k + 118]);

						//player
						if (objectid == playerActorIndex)
						{
							float angle = ReadShort(memory[k + 42], memory[k + 43]) * 360 / 1024.0f;

							angle = (540.0f - angle) % 360.0f;

							float sideAngle = (angle + 45.0f) % 90.0f - 45.0f;

							int cardinalPos = (int)Math.Floor((angle + 45.0f) / 90);

							RightText.text = string.Format("Position: {0} {1} {2}\nAngle: {3:N1} {4:N1}{5}", x, y, z, angle, sideAngle, cardinalPositions[cardinalPos % 4]);

							//check if player has moved
							if (box.transform.position != lastPlayerPosition)
							{
								//center camera to player position
								GetComponent<RoomLoader>().CenterCamera(new Vector2(box.transform.position.x, box.transform.position.z));
								lastPlayerPosition = box.transform.position;
							}

							if (Camera.main.orthographic)
							{
								//make sure player is always visible
								box.transform.localScale = new Vector3(box.transform.localScale.x, box.transform.localScale.y * 5.0f, box.transform.localScale.z);
							}

							//follow player
							Arrow.transform.position = box.transform.position + new Vector3(0.0f, box.transform.localScale.y / 2.0f + 0.001f, 0.0f);
							//face camera
							Arrow.transform.rotation = Quaternion.AngleAxis(90.0f, -Vector3.left);
							Arrow.transform.rotation *= Quaternion.AngleAxis(-angle, Vector3.forward);

							Arrow.transform.localScale = new Vector3(
								box.transform.localScale.x * 0.9f,
								box.transform.localScale.z * 0.9f,
								1.0f);

							//player is white
							box.Color = new Color32(255, 255, 255, 255);

							player = box.gameObject;
						}
						else
						{
							//other objects are green
							box.Color = new Color32(0, 128, 0, 255);
						}

						box.gameObject.SetActive(true);
					}
					else
					{
						box.gameObject.SetActive(false);
					}

					i++;
				}
			}
		}

		//arrow is only active if actors are active and player is active
		Arrow.SetActive(Actors.activeSelf
			&& player != null
			&& player.activeSelf
			&& player.transform.localScale.magnitude > 0.01f);
	}

	private short ReadShort(byte a, byte b)
	{
		unchecked
		{
			return (short)(a | b << 8);
		}
	}

	#region Room loader

	public bool LinkToDosBOX(int floor, int room)
	{
		Process process = Process.GetProcesses()
		.FirstOrDefault(x =>
		{
			string name;
			try
			{
				name = x.ProcessName; 
			}
			catch
			{ 
				name = string.Empty;
			} 
			return name.StartsWith("DOSBOX", StringComparison.InvariantCultureIgnoreCase);
		});

		if (process == null)
		{
			RightText.text = "Cannot find DOSBOX process";
			return false;
		}

		//search player position in DOSBOX
		ProcessHelper procMemory = ProcessHelper.OpenProcess(process.Id);
		for (int patterns = 0; patterns < PlayerInitialPosition.Length; patterns++)
		{
			long address = procMemory.SearchForBytePattern(PlayerInitialPosition[patterns]);
			if (address != -1)
			{
				//force reload
				linkfloor = floor;
				linkroom = room;

				memoryAddress = address;
				memoryProcess = procMemory;
				memory = new byte[ActorStructSize[patterns] * 50];
				dosBoxPattern = patterns;
				return true;
			}
		}

		RightText.text = "Cannot find player data in DOSBOX process memory.";
		return false;
	}

	public void UnlinkDosBox()
	{
		memoryProcess.Close();
		memoryProcess = null;
		RightText.text = string.Empty;
	}

	public void ResetCamera(int floor, int room)
	{
		lastPlayerPosition = Vector3.zero;
		linkfloor = floor;
		linkroom = room;
	}

	#endregion
}