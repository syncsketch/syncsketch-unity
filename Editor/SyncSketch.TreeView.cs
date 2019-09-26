using UnityEngine;
using UnityEditor;
using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;

namespace SyncSketch
{
	/// <summary>
	/// TreeView GUI for a SyncSketch account
	/// </summary>
	public class SyncSketchTreeView : TreeView
	{
		const int MinimumVisibleRows = 4;
		const int MaximumVisibleRows = 30;

		public class Item : TreeViewItem
		{
			public API.SyncSketchItem item { get; set; }

			public Item(int id, int depth, string displayName, API.SyncSketchItem item) : base(id, depth, displayName)
			{
				this.item = item;
			}
		}

		public class State : TreeViewState
		{
			public State(int defaultRowCount = 10)
			{
				this.visibleRowsCount = defaultRowCount;
			}

			[SerializeField] int _visibleRowsCount;
			public int visibleRowsCount
			{
				get { return _visibleRowsCount; }
				set
				{
					_visibleRowsCount = value;
					onVisibleRowCountChange?.Invoke(value);
				}
			}

			public Action<int> onVisibleRowCountChange;
		}

		#region Public Events

		public event Action<API.SyncSketchItem> itemDoubleClicked;
		public event Action<API.SyncSketchItem> itemContextClicked;
		public event Action<API.SyncSketchItem> itemSelected;

		#endregion

		SyncSketch.API syncSketch;
		GUIContent headerLabel;
		SearchField searchField;
		List<Item> allRows;
		Item selectedItem;
		bool selectedItemIsProjectOrReview;
		Action onSyncCallback;

		new State state { get { return (State)base.state; } }

		public SyncSketchTreeView(SyncSketch.API syncSketch, State treeViewState, string headerText = null, Action onSync = null) : base(treeViewState)
		{
			this.syncSketch = syncSketch;
			this.headerLabel = headerText != null ? new GUIContent(headerText) : null;
			this.showAlternatingRowBackgrounds = true;
			this.onSyncCallback = onSync;

			searchField = new SearchField();
			searchField.downOrUpArrowKeyPressed += SetFocusAndEnsureSelectedItem;
			Reload();
		}

		#region UI

		// height-dragging related
		bool draggingSize;
		Vector2 draggingStartPosition;
		int prevVisibleRowsCount;

		public void OnGUILayout(bool showSearchField = true)
		{
			// stop dragging on mouse up
			if (Event.current.type == EventType.MouseUp || Event.current.rawType == EventType.MouseUp)
			{
				draggingSize = false;
			}

			// header

			// Get the full rect for the current line and divide manually, else we end up having a minimum-width line
			// where contents gets pushed if the space is too narrow, making buttons pushed outside of the view
			var lineRect = EditorGUILayout.GetControlRect();
			{
				if (onSyncCallback != null)
				{
					var syncButtonRect = lineRect;
					syncButtonRect.width = 26;
					lineRect.xMax -= syncButtonRect.width;
					syncButtonRect.x = lineRect.xMax;
					lineRect.xMax -= EditorGUIUtility.standardVerticalSpacing;

					if (GUI.Button(syncButtonRect, GUIContents.SyncIcon))
					{
						onSyncCallback();
					}
				}

				var addButtonRect = lineRect;
				addButtonRect.width = 80;
				lineRect.xMax -= addButtonRect.width;
				addButtonRect.x = lineRect.xMax;
				using (GUIUtils.Enabled(selectedItem != null && selectedItemIsProjectOrReview))
				{
					if (GUI.Button(addButtonRect, "Add Review"))
					{
						OnAddItem(selectedItem.item, "New Review");
					}
				}

				if (headerLabel != null)
				{
					GUI.Label(lineRect, headerLabel, EditorStyles.boldLabel);
				}


			}

			// search field
			if (showSearchField)
			{
				var searchRect = EditorGUILayout.GetControlRect();
				this.searchString = searchField.OnToolbarGUI(searchRect, this.searchString);
			}

			// tree view
			const float rowHeight = 16;
			var rect = EditorGUILayout.GetControlRect(GUILayout.Height(rowHeight * state.visibleRowsCount));
			OnGUI(rect);

			// resize height
			var resizeRect = GUIUtils.ResizeSeparator();
			resizeRect.yMin -= 5;
			resizeRect.yMax += 5;

			if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
			{
				Event.current.Use();
				draggingSize = true;
				draggingStartPosition = Event.current.mousePosition;
				prevVisibleRowsCount = state.visibleRowsCount;
			}

			if (draggingSize)
			{
				EditorGUIUtility.AddCursorRect(new Rect(Event.current.mousePosition.x - 10, Event.current.mousePosition.y - 10, 20, 20), MouseCursor.ResizeVertical);
				if (Event.current.type == EventType.MouseDrag)
				{
					Event.current.Use();
					float delta = draggingStartPosition.y - Event.current.mousePosition.y;
					state.visibleRowsCount = Mathf.Clamp(prevVisibleRowsCount - Mathf.FloorToInt(delta / rowHeight), MinimumVisibleRows, MaximumVisibleRows);
				}
			}
			else
			{
				EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeVertical);
			}
		}

		static Color colorSelected = EditorGUIUtility.isProSkin ? new Color32(62, 95, 150, 255) : new Color32(62, 125, 231, 255);
		static Color colorSelectedDisabled = EditorGUIUtility.isProSkin ? new Color32(62, 95, 150, 128) : new Color32(62, 125, 231, 128);

		protected override void RowGUI(RowGUIArgs args)
		{
			// small hack to make the blue selected color persistent, even when tree view has lost focus
			if (args.selected && Event.current.type == EventType.Repaint)
			{
				EditorGUI.DrawRect(args.rowRect, GUI.enabled ? colorSelected : colorSelectedDisabled);
			}

			var btnRect = args.rowRect;
			btnRect.width = 24;
			btnRect.x = args.rowRect.xMax - btnRect.width;
			args.rowRect.xMax = btnRect.x;

			var review = ((Item)args.item).item as API.Review;
			if (review != null)
			{
				if (GUI.Button(btnRect, GUIContent.none, GUIStyles.ContextMenuButton))
				{
					ShowReviewContextMenu(review);
				}
			}
			else
			{
				var project = ((Item)args.item).item as API.Project;
				if (project != null)
				{
					if (GUI.Button(btnRect, GUIContent.none, GUIStyles.ContextMenuButton))
					{
						ShowProjectContextMenu(project);
					}
				}
				else
				{
					var account = ((Item)args.item).item as API.Account;
					if (account != null)
					{
						if (GUI.Button(btnRect, GUIContent.none, GUIStyles.ContextMenuButton))
						{
							ShowAccountContextMenu(account);
						}
					}
				}
			}

			base.RowGUI(args);
		}

		void ShowAccountContextMenu(API.Account account)
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Add Project..."), false, () => { OnAddItem(account, "New Project"); });
			menu.ShowAsContext();
		}

		void ShowProjectContextMenu(API.Project project)
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("View project in browser"), false, () => Application.OpenURL(project.ProjectURL));
			menu.AddItem(new GUIContent("Add Review..."), false, () => { OnAddItem(project, "New Review"); });
			menu.ShowAsContext();
		}

		void ShowReviewContextMenu(API.Review review)
		{
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("View review in browser"), false, () => Application.OpenURL(review.ReviewURL));
			menu.AddItem(new GUIContent("Add Review..."), false, () => { OnAddItem(review, "New Review"); });
			menu.ShowAsContext();
		}

		#endregion

		#region Init

		protected override TreeViewItem BuildRoot()
		{
			var root = new Item(0, -1, "Root", null);
			allRows = new List<Item>(50);

			bool insertNewReview = newItemToInsert != null && newItemSiblingId > 0;
			int depth = syncSketch.HasMultipleAccounts ? 1 : 0;
			var current = root;
			foreach (var account in syncSketch.accounts)
			{
				if (syncSketch.HasMultipleAccounts)
				{
					var accountItem = new Item(account.id, 0, account.name, account);
					root.AddChild(accountItem);
					allRows.Add(accountItem);
					ShouldInsertNewItem(accountItem, depth + 0);
					current = accountItem;
				}

				foreach (var project in account.projects)
				{
					var projectItem = new Item(project.id, depth + 0, string.IsNullOrWhiteSpace(project.name) ? "<no name>" : project.name, project);
					allRows.Add(projectItem);
					ShouldInsertNewItem(projectItem, depth + 1);

					foreach (var review in project.reviews)
					{
						var reviewItem = new Item(review.id, depth + 1, string.IsNullOrWhiteSpace(review.name) ? "<no name>" : review.name, review);

						projectItem.AddChild(reviewItem);
						allRows.Add(reviewItem);
						ShouldInsertNewItem(reviewItem, depth + 1);
					}
					current.AddChild(projectItem);
				}
			}

			return root;
		}

		protected override bool CanMultiSelect(TreeViewItem item)
		{
			return false;
		}

		#endregion

		#region Handle Add Items

		// Add item system with renaming
		const int NewItemID = int.MaxValue;
		int newItemSiblingId;
		Item newItemToInsert;
		API.SyncSketchItem newItemParent;

		/// <summary>
		/// Should a newly created item be inserted in the tree view
		/// </summary>
		/// <param name="item">The parent item to add the new item to</param>
		/// <param name="depth">New item depth</param>
		void ShouldInsertNewItem(Item item, int depth)
		{
			if (newItemSiblingId == item.id)
			{
				newItemToInsert.depth = depth;
				item.AddChild(newItemToInsert);
				allRows.Add(newItemToInsert);
				newItemParent = item.item;
			}
		}

		// Add item flow:
		// - add fake item in the tree view, begin rename on it
		// - on rename ended, actually add the new item through the API
		// - reload the tree view that will delete the fake item, and add the real one
		void OnAddItem(API.SyncSketchItem item, string defaultName)
		{
			newItemToInsert = new Item(NewItemID, 0, defaultName, null);
			newItemSiblingId = item.id;

			this.Reload();
			this.SetSelection(NewItemID);
			this.SetFocusAndEnsureSelectedItem();
			this.BeginRename(newItemToInsert);

			newItemToInsert = null;
			newItemSiblingId = 0;
		}

		protected override void RenameEnded(RenameEndedArgs args)
		{
			base.RenameEnded(args);

			if (!args.acceptedRename || string.IsNullOrEmpty(args.newName))
			{
				this.Reload();
				return;
			}

			var tempReview = allRows.Find(item => item.id == args.itemID);
			tempReview.displayName = args.newName;

			// actually add the new item using the API, and refresh the tree view

			API.SyncSketchItem newItem = null;
			var accountParent = newItemParent as API.Account;
			if (accountParent != null)
			{
				newItem = accountParent.AddProject(syncSketch, args.newName, "Created from Unity");
			}

			var projectParent = newItemParent as API.Project;
			if (projectParent != null)
			{
				newItem = projectParent.AddReview(syncSketch, args.newName, "Created from Unity");
			}

			newItemToInsert = null;
			newItemSiblingId = 0;
			newItemParent = null;
			this.Reload();

			if (newItem == null)
			{
				Log.Error("Couldn't create the new item.");
			}
			else
			{
				this.SetSelection(newItem.id);
			}
		}

		protected override bool CanRename(TreeViewItem item)
		{
			// we want to manually trigger the rename action, users can't do it anywhere
			return false;
		}

		#endregion

		#region Handle Search

		List<TreeViewItem> visibleRows = new List<TreeViewItem>(50);

		void build(int addReviewNextToId = 0, Item newItem = null)
		{

		}

		protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
		{
			visibleRows.Clear();

			if (!hasSearch)
			{
				AddChildrenRecursive(root, visibleRows);
			}
			else
			{
				Search(root, searchString, visibleRows);
			}

			return visibleRows;
		}

		public bool HasItem(int id)
		{
			return allRows == null ? false : allRows.Exists(item => item.id == id);
		}

		// TODO this is currently broken
		void Search(TreeViewItem root, string search, List<TreeViewItem> result)
		{
			foreach (var account in root.children)
			{
				// keep project if any of its children matches the search, or if the project itself matches
				bool accountAdded = false;
				bool accountMatch = false;
				bool accountExpanded = IsExpanded(account.id);

				// account matches?
				if (DoesItemMatchSearch(account, search))
				{
					accountMatch = true;
					result.Add(account);
					accountAdded = true;
				}

				if (account.hasChildren)
				{
					// any project matches? (or if the account itself matches, show all its reviews)
					foreach (var project in account.children)
					{
						bool projectAdded = false;
						bool projectMatch = false;
						bool projectExpanded = IsExpanded(project.id);

						if (DoesItemMatchSearch(project, search) || accountMatch)
						{
							projectMatch = true;

							if (!accountAdded)
							{
								result.Add(account);
								accountAdded = true;
							}

							if (accountExpanded)
							{
								result.Add(project);
								projectAdded = true;
							}
						}

						if (project.hasChildren)
						{
							// any review matches? (or if the project itself matches, show all its reviews)
							foreach (var review in project.children)
							{
								if (DoesItemMatchSearch(review, search) || projectMatch)
								{
									if (!accountAdded)
									{
										result.Add(account);
										accountAdded = true;
									}

									if (!projectAdded && accountExpanded)
									{
										result.Add(project);
										projectAdded = true;
									}

									if (accountExpanded && projectExpanded)
									{
										result.Add(review);
									}
								}
							}
						}
					}
				}
			}
		}

		void AddChildrenRecursive(TreeViewItem item, List<TreeViewItem> rows, bool includeAll = false)
		{
			if (item == null)
			{
				return;
			}

			if (item.hasChildren && ((item.depth < 0 || IsExpanded(item.id)) || includeAll))
			{
				foreach (var child in item.children)
				{
					rows.Add(child);
					AddChildrenRecursive(child, rows, includeAll);
				}
			}
		}

		protected override bool CanChangeExpandedState(TreeViewItem item)
		{
			// this ensures that the foldout arrows don't disappear when performing a search
			return item.hasChildren;
		}

		#endregion

		#region Events

		protected override void SelectionChanged(IList<int> selectedIds)
		{
			// note: selectedIds.Count should never be over 1 item (multi select disabled)
			Debug.Assert(selectedIds.Count <= 1);

			selectedItem = null;
			selectedItemIsProjectOrReview = false;
			if (selectedIds.Count > 0)
			{
				selectedItem = (Item)FindItem(selectedIds[0], rootItem);
				if (selectedItem != null)
				{
					selectedItemIsProjectOrReview = selectedItem.item is API.Project || selectedItem.item is API.Review;
				}
			}

			if (itemSelected == null)
			{
				return;
			}

			if (selectedIds.Count == 0)
			{
				itemSelected?.Invoke(null);
			}
			else
			{
				int id = selectedIds[0];
				if (id != NewItemID)
				{
					HandleItemCallbacks(id, itemSelected);
				}
			}
		}

		protected override void DoubleClickedItem(int id)
		{
			var callback = itemDoubleClicked ?? DefaultItemDoubleClick;
			HandleItemCallbacks(id, callback);
		}

		protected override void ContextClickedItem(int id)
		{
			var callback = itemContextClicked ?? DefaultItemContext;
			HandleItemCallbacks(id, callback);
		}

		void HandleItemCallbacks(int id, Action<API.SyncSketchItem> callback)
		{
			var item = GetItemFromId(id);
			if (item == null)
			{
				Log.Error("Can't find item with id: " + id);
				return;
			}

			callback(item);
		}

		// Default action for context menu on item
		void DefaultItemContext(API.SyncSketchItem item)
		{
			// make sure the right-clicked item gets selected
			this.SetSelection(item.id, TreeViewSelectionOptions.None);

			var review = item as API.Review;
			if (review != null)
			{
				ShowReviewContextMenu(review);
			}

			var project = item as API.Project;
			if (project != null)
			{
				ShowProjectContextMenu(project);
			}

			var account = item as API.Account;
			if (account != null)
			{
				ShowAccountContextMenu(account);
			}
		}

		void DefaultItemDoubleClick(API.SyncSketchItem item)
		{
			var review = item as API.Review;
			if (review != null)
			{
				Application.OpenURL(review.ReviewURL);
			}

			var project = item as API.Project;
			if (project != null)
			{
				Application.OpenURL(project.ProjectURL);
			}
		}

		#endregion

		#region Id Utilities

		public void SetSelection(int id, TreeViewSelectionOptions options = TreeViewSelectionOptions.RevealAndFrame | TreeViewSelectionOptions.FireSelectionChanged)
		{
			this.SetSelection(new int[] { id }, options);
		}

		/// <summary>
		/// Return the API.SyncSketchItem stored for this id in the tree
		/// </summary>
		API.SyncSketchItem GetItemFromId(int id)
		{
			var match = visibleRows.Find(x => x.id == id);
			if (match == null)
			{
				return null;
			}

			var item = ((Item)match).item;
			return item;
		}

		#endregion
	}
}
