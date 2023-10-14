﻿namespace VelocityMap
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Renci.SshNet;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms;
    using System.Windows.Forms.DataVisualization.Charting;
    using MotionProfile;

    using MotionProfile.SegmentedProfile;
    using Renci.SshNet.Sftp;
    using VelocityMap.Forms;
    using VelocityMap.VelocityGenerate;
    using Menu = Forms.Menu;
    using System.Runtime.Serialization.Formatters.Binary;


    /// <summary>
    /// Defines the <see cref="MotionProfiler" />
    /// </summary>
    public partial class MotionProfiler : Form
    {
        /// <summary>
        /// Defines the fieldHeight
        /// </summary>
        ///
        // OLD 2019: 8230
        private double fieldHeight = 7.908;

        /// <summary>
        /// Defines the fieldWidth
        /// </summary>
        // OLD 2019: 8230
        // 8.00354?
        private double fieldWidth = 8.016;

        internal int padding = 1;
        public List<ControlPoint> controlPointArray = new List<ControlPoint>();
        //public OutputPoints outputPoints = new OutputPoints();

        // new
        public static List<Profile> profiles = new List<Profile>();

        static List<List<Profile>> undo = new List<List<Profile>>();
        static List<List<Profile>> redo = new List<List<Profile>>();


        public int newProfileCount = 0;
        public int newPathCount = 0;
        public double pointSize = 0.1;
        public bool splineMode = false;

        Profile selectedProfile = null;
        ProfilePath selectedPath = null;
        ControlPoint placingPoint = null;

        ControlPoint clickedPoint = null;
        ProfilePath clickedPointPath = null;

        ControlPoint preMoveClickedPoint = null;
        ProfilePath preMoveClickedPointPath = null;
        List<Profile> preMoveList = null;

        ControlPoint snappedPoint = null;
        ProfilePath snappedPointPath = null;

        private DateTime timeOfUpload;

        bool editing = false;
        int editedCell = -1;
        bool skipUpdate = false;

        Menu menu;


        /// <summary>
        /// Initializes a new instance of the <see cref="MotionProfiler"/> class.
        /// </summary>
        public MotionProfiler(Menu menu)
        {
            this.menu = menu;
            InitializeComponent();
        }

        /// <summary>
        /// Load Form 1
        /// </summary>
        private void MainForm_Load(object sender, EventArgs e)
        {
            SetupMainField();


        }

        /// <summary>
        /// Configures what the main field looks like.
        /// </summary>
        private void SetupMainField()
        {
            mainField.ChartAreas["field"].Axes[0].Minimum = 0;
            mainField.ChartAreas["field"].Axes[0].Maximum = fieldWidth;
            mainField.ChartAreas["field"].Axes[0].Interval = 1;
            mainField.ChartAreas["field"].Axes[0].IsReversed = true;

            mainField.ChartAreas["field"].Axes[1].Minimum = 0;
            mainField.ChartAreas["field"].Axes[1].Maximum = fieldHeight;
            mainField.ChartAreas["field"].Axes[1].Interval = 1;

            mainField.Series["background"].Points.AddXY(0, 0);
            mainField.Series["background"].Points.AddXY(fieldWidth, fieldHeight);

            mainField.Images.Add(new NamedImage("red", new Bitmap(VelocityMap.Properties.Resources._2023_red)));
            mainField.Images.Add(new NamedImage("red-colored", new Bitmap(VelocityMap.Properties.Resources._2023_red_colored)));
            mainField.Images.Add(new NamedImage("blue", new Bitmap(VelocityMap.Properties.Resources._2023_blue)));
            mainField.Images.Add(new NamedImage("blue-colored", new Bitmap(VelocityMap.Properties.Resources._2023_blue_colored)));
            mainField.ChartAreas["field"].BackImageWrapMode = ChartImageWrapMode.Scaled;

            if(VelocityMap.Properties.Settings.Default.defaultAllianceIsRed)
                mainField.ChartAreas["field"].BackImage = "red";
            else
                mainField.ChartAreas["field"].BackImage = "blue";
        }

        private void setBackground(bool blue, bool colored)
        {
            mainField.ChartAreas["field"].BackImage =
                (blue ? "blue" : "red") + (colored ? "-colored" : "");
        }

        private void selectPoint(int index)
        {
            ControlPointTable.Rows[index].Selected = true;
        }

        private void MainField_MouseClick(object sender, MouseEventArgs e)
        {
            Console.WriteLine("MouseClick");
            if (clickedPoint != null || e.Button != MouseButtons.Left) return;
            if (noSelectedProfile() || noSelectedPath()) return;

            if (placingPoint == null)
            {
                Chart chart = (Chart)sender;
                double x = Math.Round((double)chart.ChartAreas[0].AxisX.PixelPositionToValue(e.X), 3);
                double y = Math.Round((double)chart.ChartAreas[0].AxisY.PixelPositionToValue(e.Y), 3);

                if (x > 0 && y > 0 && x <= fieldWidth && y <= fieldHeight)
                {
                    placingPoint = new ControlPoint(selectedPath, x, y, 0);

                    ControlPointTable.Rows.Add(Math.Round(placingPoint.X, 3), Math.Round(placingPoint.Y, 3), placingPoint.Rotation);
                    selectPoint(ControlPointTable.Rows.Count - 1);
                    DrawPoint(placingPoint, selectedPath);
                }
            }
            else
            {

                selectedPath.addControlPoint(placingPoint);
                placingPoint = null;

                if (selectedPath != selectedProfile.Paths.Last()
                        && selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) + 1].SnapToPrevious)
                {
                    selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) + 1].snap(selectedPath);
                }
                UpdateField();
            }

        }

        private void mainField_MouseUp(object sender, MouseEventArgs e)
        {
            if (clickedPoint != null)
            {
                if(!clickedPoint.Equals(preMoveClickedPoint))
                {
                    saveUndoState(true, preMoveList) ;
                }
                selectedProfile.forceEdit();
                clickedPoint = null;
                clickedPointPath = null;
                preMoveClickedPoint = null;
                preMoveClickedPointPath = null;
                preMoveList = null;
                snappedPoint = null;
                snappedPointPath = null;
                
                UpdateField();
            }
            System.Windows.Forms.Cursor.Clip = new Rectangle();


        }

        /// <summary>
        /// Checks for a point selection for clicking and dragging.
        /// </summary>
        private void MainField_MouseDown(object sender, MouseEventArgs e)
        {
            if (placingPoint != null || !e.Button.HasFlag(MouseButtons.Left)) return;
            if (noSelectedProfile() || noSelectedPath()) return;

            Chart chart = (Chart)sender;

            Point p = new Point((int)chart.ChartAreas[0].AxisX.ValueToPixelPosition(fieldWidth - .01), (int)chart.ChartAreas[0].AxisY.ValueToPixelPosition(fieldHeight - .02));

            p = chart.PointToScreen(p);

            System.Drawing.Rectangle bounds = new System.Drawing.Rectangle(p.X, p.Y, (int)chart.ChartAreas[0].AxisX.ValueToPixelPosition(0) - (int)chart.ChartAreas[0].AxisX.ValueToPixelPosition(fieldWidth - .01), (int)chart.ChartAreas[0].AxisY.ValueToPixelPosition(0) - (int)chart.ChartAreas[0].AxisY.ValueToPixelPosition(fieldHeight - .02));
            System.Windows.Forms.Cursor.Clip = bounds;

            double clickedX = Math.Round((double)chart.ChartAreas[0].AxisX.PixelPositionToValue(e.X),3);
            double clickedY = Math.Round((double)chart.ChartAreas[0].AxisY.PixelPositionToValue(e.Y),3);

            foreach (ProfilePath path in selectedProfile.Paths)
            {
                foreach (ControlPoint point in path.ControlPoints)
                {
                    if (Math.Abs(clickedX - point.X) < pointSize && Math.Abs(clickedY - point.Y) < pointSize)
                    {
                        clickedPoint = point;
                        clickedPointPath = path;

                        preMoveClickedPoint = new ControlPoint(point, path);
                        preMoveClickedPointPath = new ProfilePath(path, selectedProfile);

                        List<Profile> ps = new List<Profile>();
                        foreach (Profile pro in profiles)
                        {
                            ps.Add(new Profile(pro));
                        }
                        preMoveList = ps;


                        if (clickedPointPath == selectedPath) selectPoint(path.ControlPoints.IndexOf(point));

                        if (clickedPoint == clickedPointPath.ControlPoints[0]
                            && clickedPointPath.SnapToPrevious)
                        {
                            int pathIndex = selectedProfile.Paths.IndexOf(clickedPointPath);
                            snappedPointPath = selectedProfile.Paths[pathIndex - 1];
                            snappedPoint = snappedPointPath.ControlPoints.Last();
                        }
                        else if (clickedPoint == clickedPointPath.ControlPoints.Last()
                            && clickedPointPath != selectedProfile.Paths.Last())
                        {
                            int pathIndex = selectedProfile.Paths.IndexOf(clickedPointPath);
                            snappedPointPath = selectedProfile.Paths[pathIndex + 1];
                            if (selectedProfile.Paths[pathIndex + 1].SnapToPrevious)
                                snappedPoint = snappedPointPath.ControlPoints[0];
                        }
                    }
                }
            }

        }

        /// <summary>
        /// The event that is called when the user mouse while above the main field.
        /// </summary>
        /// 

        private void MainField_MouseMove(object sender, MouseEventArgs e)
        {
            
            Chart chart = (Chart)sender;

            //if the user is holding the left button while moving the mouse allow them to move the point.
            if (clickedPoint != null && e.Button.HasFlag(MouseButtons.Left))
            {
                double newX = 0.0;
                double newY = 0.0;
                try
                {

                    newX = Math.Round((double)chart.ChartAreas[0].AxisX.PixelPositionToValue(e.X),3);
                    newY = Math.Round((double)chart.ChartAreas[0].AxisY.PixelPositionToValue(e.Y),3);

                }
                catch
                {
                    return;
                }

                clickedPoint.quickChangeX(newX);
                clickedPoint.quickChangeY(newY);
                if (snappedPoint != null)
                {
                    snappedPoint.quickChangeX(newX);
                    snappedPoint.quickChangeY(newY);
                }

                if (clickedPointPath == selectedPath)
                {
                    ControlPointTable.SelectedRows[0].Cells[0].Value = Math.Round(newX, 3);
                    ControlPointTable.SelectedRows[0].Cells[1].Value = Math.Round(newY, 3);
                }

                DrawPath(clickedPointPath, true);
                resetTrackBar();
                if (snappedPoint != null) DrawPath(snappedPointPath, true);
                Console.WriteLine("MouseMove");
            }
            else
            {
                System.Windows.Forms.Cursor.Clip = new Rectangle();
            }
            if (placingPoint != null)
            {

                Point p = new Point((int)chart.ChartAreas[0].AxisX.ValueToPixelPosition(fieldWidth - .01), (int)chart.ChartAreas[0].AxisY.ValueToPixelPosition(fieldHeight - .02));

                p = chart.PointToScreen(p);

                System.Drawing.Rectangle bounds = new System.Drawing.Rectangle(p.X, p.Y, (int)chart.ChartAreas[0].AxisX.ValueToPixelPosition(0) - (int)chart.ChartAreas[0].AxisX.ValueToPixelPosition(fieldWidth - .01), (int)chart.ChartAreas[0].AxisY.ValueToPixelPosition(0) - (int)chart.ChartAreas[0].AxisY.ValueToPixelPosition(fieldHeight - .02));
                System.Windows.Forms.Cursor.Clip = bounds;

                double x = Math.Round((double)chart.ChartAreas[0].AxisX.PixelPositionToValue(e.X),3);
                double y = Math.Round((double)chart.ChartAreas[0].AxisY.PixelPositionToValue(e.Y),3);

                placingPoint.quickChangeRotation((int)(Math.Atan2(x - placingPoint.X, y - placingPoint.Y) * 180 / Math.PI));
                ControlPointTable.Rows[ControlPointTable.Rows.Count - 1].Cells[2].Value = placingPoint.Rotation;

                mainField.Series[placingPoint.Id].Points.Clear();
                double x1 = (double)(placingPoint.X + pointSize * Math.Sin((placingPoint.Rotation) * Math.PI / 180));
                double y1 = (double)(placingPoint.Y + pointSize * Math.Cos((placingPoint.Rotation) * Math.PI / 180));
                mainField.Series[placingPoint.Id].Points.AddXY(x1, y1);
                double x2 = (double)(placingPoint.X + 0.600 * Math.Sin((placingPoint.Rotation) * Math.PI / 180));
                double y2 = (double)(placingPoint.Y + 0.600 * Math.Cos((placingPoint.Rotation) * Math.PI / 180));
                mainField.Series[placingPoint.Id].Points.AddXY(x2, y2);
            }
        }

        private void ControlPoints_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                double newValue = double.Parse(ControlPointTable.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString());
                switch (e.ColumnIndex)
                {
                    case 0:
                        selectedPath.ControlPoints[e.RowIndex].X = newValue;
                        break;
                    case 1:
                        selectedPath.ControlPoints[e.RowIndex].Y = newValue;
                        break;
                    case 2:
                        selectedPath.ControlPoints[e.RowIndex].Rotation = (int)newValue;
                        break;
                }
                if (e.RowIndex == 0 && selectedPath.SnapToPrevious)
                    selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) - 1].snapLast(selectedPath.ControlPoints[0]);
                if (e.RowIndex == selectedPath.ControlPoints.Count - 1 && selectedPath != selectedProfile.Paths.Last()
                        && selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) + 1].SnapToPrevious)
                {
                    selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) + 1].snap(selectedPath);
                }
                UpdateField();
            }
            catch (Exception)
            {
                setStatus("Data values must be numbers", true);
                switch (e.ColumnIndex)
                {
                    case 0:
                        ControlPointTable.Rows[e.RowIndex].Cells[e.ColumnIndex].Value
                            = Math.Round(selectedPath.ControlPoints[e.RowIndex].X, 3);
                        break;
                    case 1:
                        ControlPointTable.Rows[e.RowIndex].Cells[e.ColumnIndex].Value
                            = Math.Round(selectedPath.ControlPoints[e.RowIndex].Y, 3);
                        break;
                    case 2:
                        ControlPointTable.Rows[e.RowIndex].Cells[e.ColumnIndex].Value
                            = selectedPath.ControlPoints[e.RowIndex].Rotation;
                        break;
                }
            }
        }

        private void DrawPoint(ControlPoint point, ProfilePath path)
        {
            mainField.Series[path.Id + "-points"].Points.AddXY(point.X, point.Y);
            if (path == selectedPath)
            {
                mainField.Series[path.Id + "-points"].Points.Last().Label =
                    mainField.Series[path.Id + "-points"].Points.Count.ToString();
            }

            int seriesIndex = mainField.Series.IndexOf(point.Id);
            if (seriesIndex != -1) mainField.Series.RemoveAt(seriesIndex);

            mainField.Series.Add(point.Id);
            mainField.Series[point.Id].ChartType = SeriesChartType.Line;
            mainField.Series[point.Id].BorderWidth = 2;
            mainField.Series[point.Id].Color = path == selectedPath ? Color.Red : Color.DarkRed;

            double x1 = (double)(point.X + pointSize * Math.Sin((point.Rotation) * Math.PI / 180));
            double y1 = (double)(point.Y + pointSize * Math.Cos((point.Rotation) * Math.PI / 180));
            mainField.Series[point.Id].Points.AddXY(x1, y1);
            double x2 = (double)(point.X + (path == selectedPath ? 0.6 : 0.3) * Math.Sin((point.Rotation) * Math.PI / 180));
            double y2 = (double)(point.Y + (path == selectedPath ? 0.6 : 0.3) * Math.Cos((point.Rotation) * Math.PI / 180));
            mainField.Series[point.Id].Points.AddXY(x2, y2);

            if (point == placingPoint) return;
            mainField.Series[path.Id + "-path"].Points.AddXY(point.X, point.Y);

            if (path == selectedPath)
            {
                int seriesIndex1 = mainField.Series.IndexOf(point.Id + "Rectangle");
                if (seriesIndex1 != -1) mainField.Series.RemoveAt(seriesIndex1);
                mainField.Series.Add(point.Id + "Rectangle");
                mainField.Series[point.Id + "Rectangle"].ChartType = SeriesChartType.Line;
                mainField.Series[point.Id + "Rectangle"].BorderWidth = 2;
                mainField.Series[point.Id + "Rectangle"].Color = Color.GreenYellow;

                Translation2d fl = new Translation2d(-Properties.Settings.Default.FrameLength / 2, Properties.Settings.Default.FrameWidth / 2).rotateBy(Rotation2d.fromDegrees(-point.Rotation)).plus(new Translation2d(point.X, point.Y));
                Translation2d fr = new Translation2d(Properties.Settings.Default.FrameLength / 2, Properties.Settings.Default.FrameWidth / 2).rotateBy(Rotation2d.fromDegrees(-point.Rotation)).plus(new Translation2d(point.X, point.Y));
                Translation2d br = new Translation2d(Properties.Settings.Default.FrameLength / 2, -Properties.Settings.Default.FrameWidth / 2).rotateBy(Rotation2d.fromDegrees(-point.Rotation)).plus(new Translation2d(point.X, point.Y));
                Translation2d bl = new Translation2d(-Properties.Settings.Default.FrameLength / 2, -Properties.Settings.Default.FrameWidth / 2).rotateBy(Rotation2d.fromDegrees(-point.Rotation)).plus(new Translation2d(point.X, point.Y));

                /*mainField.Series[point.Id + "Rectangle"].Points.AddXY(fl.getX(), fl.getY());
                mainField.Series[point.Id + "Rectangle"].Points.AddXY(fr.getX(), fr.getY());
                mainField.Series[point.Id + "Rectangle"].Points.AddXY(br.getX(), br.getY());
                mainField.Series[point.Id + "Rectangle"].Points.AddXY(bl.getX(), bl.getY());
                mainField.Series[point.Id + "Rectangle"].Points.AddXY(fl.getX(), fl.getY());*/

            }




            /*mainField.Annotations.Add(new TextAnnotation() 
                {
                    Text = (path.ControlPoints.IndexOf(point) + 1).ToString(),
                    Alignment = ContentAlignment.MiddleCenter,
                    ForeColor = Color.White,
                    X = point.X,
                    Y = point.Y
                }
            );*/
        }

        private void DrawPath(ProfilePath path, bool quickDraw)
        {
            if (!showPathsCheckbox.Checked && path != selectedPath) return;

            int seriesIndex = mainField.Series.IndexOf(path.Id + "-path");
            if (seriesIndex != -1) mainField.Series.RemoveAt(seriesIndex);
            seriesIndex = mainField.Series.IndexOf(path.Id + "-points");
            if (seriesIndex != -1) mainField.Series.RemoveAt(seriesIndex);
            seriesIndex = mainField.Series.IndexOf(path.Id + "-padding");
            if (seriesIndex != -1) mainField.Series.RemoveAt(seriesIndex);


            mainField.Series.Add(path.Id + "-path");
            mainField.Series[path.Id + "-path"].ChartArea = "field";
            mainField.Series[path.Id + "-path"].ChartType = SeriesChartType.Point;
            mainField.Series[path.Id + "-path"].Color = path == selectedPath ? Color.Aqua : Color.Blue;
            mainField.Series[path.Id + "-path"].MarkerSize = 2;
            mainField.Series[path.Id + "-path"].BorderWidth = 2;

            mainField.Series.Add(path.Id + "-points");
            mainField.Series[path.Id + "-points"].ChartArea = "field";
            mainField.Series[path.Id + "-points"].ChartType = SeriesChartType.Point;
            mainField.Series[path.Id + "-points"].Color = path == selectedPath ? Color.Lime : Color.Green;
            mainField.Series[path.Id + "-points"].MarkerSize = 10;
            mainField.Series[path.Id + "-points"].MarkerStyle = MarkerStyle.Diamond;
            mainField.Series[path.Id + "-points"].LabelForeColor = Color.White;




            foreach (ControlPoint point in path.ControlPoints)
            {
                DrawPoint(point, path);
            }

            if (path.ControlPoints.Count < 2) return;

            seriesIndex = mainField.Series.IndexOf(path.Id + "-left");
            if (seriesIndex != -1) mainField.Series.RemoveAt(seriesIndex);
            seriesIndex = mainField.Series.IndexOf(path.Id + "-right");
            if (seriesIndex != -1) mainField.Series.RemoveAt(seriesIndex);

            path.generate(quickDraw);


            kinematicsChart.Series["Position"].Points.Clear();
            kinematicsChart.Series["Velocity"].Points.Clear();
            kinematicsChart.Series["Acceleration"].Points.Clear();

            foreach (State s in path.getPoints())
            {
                double time = s.getTime();
                kinematicsChart.Series["Position"].Points.AddXY(time, s.getPathState().getDistance());
                kinematicsChart.Series["Velocity"].Points.AddXY(time, s.getVelocity());
                kinematicsChart.Series["Acceleration"].Points.AddXY(time, s.getAcceleration());


                Pose2d state = s.getPathState().getPose2d();

                mainField.Series[path.Id + "-path"].Points.AddXY(state.getX(), state.getY());

            }
        }



        public void UpdateField()
        {
            if (skipUpdate)
                return;
            Console.WriteLine("Update Field");
            kinematicsChart.Series["Position"].Points.Clear();
            kinematicsChart.Series["Velocity"].Points.Clear();
            kinematicsChart.Series["Acceleration"].Points.Clear();
            //AngleChart.Series["Angle"].Points.Clear();
            for (int series = 1; series < mainField.Series.Count; series++)
            {
                mainField.Series[series].Points.Clear();
            }

            if (noSelectedProfile()) return;

            if (placingPoint != null)
            {
                DrawPoint(placingPoint, selectedPath);
            }

            foreach (ProfilePath path in selectedProfile.Paths)
            {
                if (path == selectedPath) continue;
                DrawPath(path, false);
            }
            if (!noSelectedPath()) DrawPath(selectedPath, false);

            setStatus("", false);

            resetTrackBar();

        }

        private void SaveAllProfiles(object sender, EventArgs e)
        {
            if (profiles.Count == 0) return;

            FolderBrowserDialog browser = new FolderBrowserDialog();
            browser.Description = "Save all motion profiles to folder\n\nCAUTION: Existing profiles will be overridden!";

            if (browser.ShowDialog() != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            setStatus("Saving profiles to file system...", false);
            foreach (Profile profile in profiles)
            {
                string mpPath = System.IO.Path.Combine(browser.SelectedPath, profile.Name.Replace(' ', '_') + ".mp");
                string javaPath = System.IO.Path.Combine(browser.SelectedPath, profile.Name.Replace(' ', '_') + ".java");
                using (var writer = new StreamWriter(mpPath))
                {
                    writer.Write(profile.toJSON().ToString());
                }
                using (var writer = new StreamWriter(javaPath))
                {
                    writer.Write(profile.toJava());
                }
            }
            setStatus("Profiles saved to file system", false);
            Cursor = Cursors.Default;
        }

        /// <summary>
        /// Save the selected motion profile to a file.
        /// </summary>
        private void SaveSelectedProfile(object sender, EventArgs e)
        {
            if (noSelectedProfile()) return;

            SaveFileDialog browser = new SaveFileDialog();
            browser.RestoreDirectory = true;
            browser.FileName = selectedProfile.Name.Replace(' ', '_');
            browser.Filter = "Motion Profile|*.mp;";
            browser.Title = "Save motion profile file";

            if (browser.ShowDialog() != DialogResult.OK || browser.FileName.Trim().Length <= 3) return;

            Cursor = Cursors.WaitCursor;
            setStatus("Saving profile to file system...", false);
            // Write mp file to load from
            string filePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(browser.FileName.Trim()),
                System.IO.Path.GetFileNameWithoutExtension(browser.FileName.Trim()) + ".mp"
            );
            using (var writer = new StreamWriter(filePath))
            {
                writer.Write(selectedProfile.toJSON().ToString());
            }

            // Write java file to pre-compile into robot
            string pointPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(browser.FileName.Trim()),
                System.IO.Path.GetFileNameWithoutExtension(browser.FileName.Trim()) + ".java"
            );
            using (var writer = new StreamWriter(pointPath))
            {
                writer.Write(selectedProfile.toJava());
            }

            setStatus("Profile saved to file system", false);
            Cursor = Cursors.Default;
        }

        /// <summary>
        /// Loads profile json files into the profiler from a file dialog
        /// </summary>
        private void LoadProfilesFromFiles(object sender, EventArgs e)
        {
            openFilesDialog.InitialDirectory = System.Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);
            openFilesDialog.FileName = "";
            openFilesDialog.Filter = "MotionProfile Data (*.mp)|*.mp";
            openFilesDialog.Title = "Select motion profile files to load";
            openFilesDialog.Multiselect = true;

            if (openFilesDialog.ShowDialog() != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            setStatus("Loading profiles from local files...", false);
            foreach (string filename in openFilesDialog.FileNames)
            {
                using (StreamReader fileReader = new StreamReader(filename))
                {
                    try
                    {
                        profiles.Add(new Profile(JObject.Parse(fileReader.ReadToEnd())));
                        profileTable.Rows.Add(profiles.Last().Name, profiles.Last().Edited);
                    }
                    catch
                    {
                        MessageBox.Show("Error loading file " + filename, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            setStatus("Profiles loaded", false);
            Cursor = Cursors.Default;
        }

        private void LoadProfilesFromRIO(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            ConnectionInfo info = new ConnectionInfo(Properties.Settings.Default.IpAddress,
                Properties.Settings.Default.Username, new PasswordAuthenticationMethod(Properties.Settings.Default.Username, Properties.Settings.Default.Password));

            info.Timeout = TimeSpan.FromSeconds(5);

            SftpClient sftp = new SftpClient(info);
            /*SftpClient sftp = new SftpClient(
                Properties.Settings.Default.IpAddress,
                Properties.Settings.Default.Username,
                Properties.Settings.Default.Password
            );*/
            try
            {
                setStatus("Establishing RIO connection...", false);
                sftp.Connect();

                if (!sftp.Exists(Properties.Settings.Default.RioLocation))
                {
                    sftp.CreateDirectory(Properties.Settings.Default.RioLocation);
                    setStatus("No motion profiles found at RIO directory", false);
                    return;
                }

                bool foundFiles = false;
                foreach (SftpFile file in sftp.ListDirectory(Properties.Settings.Default.RioLocation))
                {
                    if (!file.Name.Contains(".mp")) continue;
                    foundFiles = true;

                    StreamReader reader = sftp.OpenText(file.FullName);
                    profiles.Add(new Profile(JObject.Parse(reader.ReadToEnd())));
                    profileTable.Rows.Add(profiles.Last().Name, profiles.Last().Edited);
                }
                if (foundFiles) setStatus("Profiles loaded from RIO", false);
                else setStatus("No motion profiles found at RIO directory", false);

                sftp.Disconnect();
            }
            catch (Renci.SshNet.Common.SshConnectionException exception)
            {
                Console.WriteLine("SshConnectionException, source: {0}", exception.StackTrace);
                setStatus("Failed to establish connection", true);
            }
            catch (Renci.SshNet.Common.SshOperationTimeoutException exception)
            {
                Console.WriteLine("SshConnectionException, source: {0}", exception.StackTrace);
                setStatus("Failed to establish connection", true);
            }
            catch (System.Net.Sockets.SocketException exception)
            {
                Console.WriteLine("SocketException, source: {0}", exception.StackTrace);
                setStatus("Failed to establish connection", true);
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException exception)
            {
                Console.WriteLine("SftpPermissionDeniedException, source: {0}", exception.StackTrace);
                setStatus("Permission denied", true);
            }
            catch (Exception exception)
            {
                Console.WriteLine("Exception, source: {0}", exception.StackTrace);
                setStatus("Failed to load RIO profiles", true);
            }

            Cursor = Cursors.Default;
        }

        private void GridCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (GridCheckBox.CheckState == CheckState.Unchecked)
            {
                mainField.ChartAreas[0].AxisX.MajorGrid.Enabled = false;
                mainField.ChartAreas[0].AxisY.MajorGrid.Enabled = false;
            }
            else
            {
                mainField.ChartAreas[0].AxisX.MajorGrid.Enabled = true;
                mainField.ChartAreas[0].AxisY.MajorGrid.Enabled = true;
            }
        }

        private void selectProfile(int index = -1)
        {
            // -1 reselects the current profile
            if (index != -1) selectedProfile = profiles[index];
            pathTable.Rows.Clear();

            if (placingPoint != null) placingPoint = null;

            if (!noSelectedProfile())
            {
                foreach (ProfilePath path in selectedProfile.Paths)
                {
                    pathTable.Rows.Add(path.Name);
                }
                profileTable.Rows[profiles.IndexOf(selectedProfile)].Selected = true;
            }
            if (index == -1 || selectedProfile.PathCount == 0) selectPath();
            else selectPath(0);


            if (!noSelectedProfile())
                setAllianceMode(selectedProfile.isRed);
        }

        private void profileTable_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (editing && profiles.Count > 0)
            {
                editing = false;
                selectProfile(editedCell);
            }
            else selectProfile(e.RowIndex);
        }

        private void newProfileButton_Click(object sender, EventArgs e)
        {
            saveUndoState();
            profiles.Add(new Profile());
            profileTable.Rows.Add(profiles.Last().Name, profiles.Last().Edited);

            selectProfile(profiles.Count - 1);
        }

        private void deleteProfileButton_Click(object sender, EventArgs e)
        {
            if (profiles.Count == 0)
            {
                editing = false;
                profileTable.ClearSelection();
                return;
            }
            if (editing) editedCell--;

            int profileIndex = profiles.IndexOf(selectedProfile);
            profiles.RemoveAt(profileIndex);
            profileTable.Rows.RemoveAt(profileIndex);
            selectProfile(Math.Min(profileIndex, profiles.Count - 1));
        }

        private void profileTable_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            profileTable.BeginEdit(false);
        }

        private void profileTable_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            profiles[e.RowIndex].Name = profileTable.Rows[e.RowIndex].Cells[0].Value.ToString();

            if (e.RowIndex == profileTable.RowCount - 1) return;
            editing = true;
            editedCell = e.RowIndex;
        }

        private void editProfileButton_Click(object sender, EventArgs e)
        {
            if (!noSelectedProfile() && profileTable.CurrentCell != null) profileTable.BeginEdit(false);
        }

        private void newPathButton_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile()) return;

            string newPathName = "Path " + (selectedProfile.Paths.Count+1);

            if (Properties.Settings.Default.SnapNewPaths && selectedProfile.Paths.Count > 0)
                selectedProfile.newPath(newPathName, splineMode, selectedProfile.Paths.Last());
            else selectedProfile.newPath(newPathName, splineMode);

            int newIndex = pathTable.Rows.Add(newPathName);
            selectPath(newIndex);
            
        }

        private void deletePathButton_Click(object sender, EventArgs e)
        {
            if (profiles.Count == 0 || profiles[profileTable.SelectedRows[0].Index].PathCount == 0)
            {
                editing = false;
                pathTable.ClearSelection();
                return;
            }

            if (editing) editedCell--;

            saveUndoState();

            int pathIndex = selectedProfile.Paths.IndexOf(selectedPath);
            selectedProfile.Paths.RemoveAt(pathIndex);
            pathTable.Rows.RemoveAt(pathIndex);
            selectProfile();
            selectPath(Math.Min(pathIndex, selectedProfile.Paths.Count - 1));
            
        }

        private void pathTable_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (pathTable.Rows[e.RowIndex].Cells[0].Value.ToString().Trim() == "")
            {
                pathTable.Rows[e.RowIndex].Cells[0].Value = selectedProfile.Paths[e.RowIndex].Name;
            }
            else
            {
                ProfilePath editedPath = selectedProfile.Paths[e.RowIndex];
                editedPath.Name = pathTable.Rows[e.RowIndex].Cells[0].Value.ToString().Trim();
            }
            editing = true;
            editedCell = e.RowIndex;
            
        }

        private void pathOrderUp_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile() || noSelectedPath()) return;

            ProfilePath tempPath = selectedPath;
            selectedProfile.movePathOrderUp(selectedPath);
            selectProfile();
            selectPath(selectedProfile.Paths.IndexOf(tempPath));
            
        }

        private void pathOrderDown_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile() || noSelectedPath()) return;

            selectedProfile.movePathOrderDown(selectedPath);
            ProfilePath tempPath = selectedPath;
            selectProfile();
            selectPath(selectedProfile.Paths.IndexOf(tempPath));
            
        }

        /// <summary>
        /// Set the status message at the top of the field display
        /// </summary>
        private void setStatus(string message, bool error)
        {
            infoLabel.Text = message;
            infoLabel.ForeColor = error ? Color.Red : Color.Black;
        }

        private bool noSelectedProfile()
        {
            if (profiles.IndexOf(selectedProfile) == -1)
            {
                setStatus("Create or select a profile", true);
                return true;
            }
            return false;
        }

        private bool noSelectedPath()
        {
            if (noSelectedProfile()) return true;
            if (selectedProfile.Paths.IndexOf(selectedPath) == -1)
            {
                setStatus("Create or select a path", true);
                return true;
            }
            return false;
        }

        private bool noPointsInPath()
        {
            if (noSelectedProfile() || noSelectedPath()) return true;
            if (selectedPath.isEmpty())
            {
                setStatus("Click on the field to create points", true);
                return true;
            }
            return false;
        }

        private void pathTable_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (editing && selectedProfile.PathCount > 0)
            {
                editing = false;
                selectPath(editedCell);
            }
            else selectPath(e.RowIndex);
        }

        private void selectPath(int index = -1)
        {
            // -1 reselects current path i think
            ControlPointTable.Rows.Clear();

            if (placingPoint != null) placingPoint = null;

            if (index != -1) selectedPath = selectedProfile.Paths[index];

            if (!noSelectedPath())
                setSplineMode(selectedPath.IsSpline);


            if (!noSelectedPath())
            {
                foreach (ControlPoint point in selectedPath.ControlPoints)
                {
                    ControlPointTable.Rows.Add(Math.Round(point.X, 3), Math.Round(point.Y, 3), point.Rotation);
                }
                pathTable.Rows[selectedProfile.Paths.IndexOf(selectedPath)].Selected = true;
            }

            if (!noPointsInPath()) selectPoint(ControlPointTable.Rows.Count - 1);

            UpdateField();
        }

        private void pathTable_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            pathTable.BeginEdit(false);
        }

        private void showPathsCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateField();
        }

        private void rioConectionButton_Click(object sender, EventArgs e)
        {
            Settings settings = new Settings();
            settings.ShowDialog();
        }

        private void editPathButton_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile() || noSelectedPath()) return;

            PathSettings settings = new PathSettings(selectedPath, pathTable.Rows[selectedProfile.Paths.IndexOf(selectedPath)].Cells[0]);
            settings.ShowDialog();
            DrawPath(selectedPath, false);
        }

        public static void updateEditTime(Profile p)
        {
            if(profiles.Contains(p))
                profileTable.Rows[profiles.IndexOf(p)].Cells[1].Value = p.Edited;
        }

        private void saveToRioButton_Click(object sender, EventArgs e)
        {
            if (profiles.Count == 0)
            {
                setStatus("No profiles to save to RIO", true);
                return;
            }
            Cursor = Cursors.WaitCursor;

            SftpClient sftp = new SftpClient(
                Properties.Settings.Default.IpAddress,
                Properties.Settings.Default.Username,
                Properties.Settings.Default.Password
            );

            try
            {
                setStatus("Establishing RIO connection...", false);
                sftp.Connect();

                if (!sftp.Exists(Properties.Settings.Default.RioLocation)) sftp.CreateDirectory(Properties.Settings.Default.RioLocation);

                List<Profile> invalidProfiles = new List<Profile>();
                foreach (Profile profile in profiles)
                {
                    if (!profile.isValid())
                    {
                        invalidProfiles.Add(profile);
                        continue;
                    }
                    // Upload txt file for robot to read in auton
                    MemoryStream txtStream = new MemoryStream(Encoding.UTF8.GetBytes(profile.toTxt().ToString()));
                    sftp.UploadFile(txtStream, System.IO.Path.Combine(
                        Properties.Settings.Default.RioLocation,
                        profile.Name.Replace(' ', '_') + ".txt"
                    ));
                    // Upload mp file for profiler to read for editing
                    MemoryStream mpStream = new MemoryStream(Encoding.UTF8.GetBytes(profile.toJSON().ToString()));
                    sftp.UploadFile(mpStream, System.IO.Path.Combine(
                        Properties.Settings.Default.RioLocation,
                        profile.Name.Replace(' ', '_') + ".mp"
                    ));
                }

                setStatus("Verifying file contents...", false);
                bool verified = true;
                foreach (Profile profile in profiles)
                {
                    if (invalidProfiles.Contains(profile)) continue;
                    StreamReader reader = sftp.OpenText(
                        System.IO.Path.Combine(Properties.Settings.Default.RioLocation, profile.Name.Replace(' ', '_') + ".txt")
                    );
                    if (profile.toTxt() != reader.ReadToEnd()) verified = false;
                }

                if (invalidProfiles.Count > 0) MessageBox.Show(
                    "One or more profiles were not deployed due to being invalid",
                    "Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                if (verified)
                {
                    setStatus("Profile(s) uploaded and verified successfully", false);
                    timeOfUpload = DateTime.Now;
                }
                else setStatus("Failed to verify uploaded file content", true);
                sftp.Disconnect();
            }
            catch (Renci.SshNet.Common.SshConnectionException exception)
            {
                Console.WriteLine("SshConnectionException, source: {0}", exception.StackTrace);
                setStatus("Failed to establish connection", true);
            }
            catch (System.Net.Sockets.SocketException exception)
            {
                Console.WriteLine("SocketException, source: {0}", exception.StackTrace);
                setStatus("Failed to establish connection", true);
            }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException exception)
            {
                Console.WriteLine("SftpPermissionDeniedException, source: {0}", exception.StackTrace);
                setStatus("Permission denied", true);
            }
            catch (Exception exception)
            {
                Console.WriteLine("Exception, source: {0}", exception.StackTrace);
                setStatus("Failed to upload profile to RIO", true);
            }

            Cursor = Cursors.Default;
        }

        private void defaultsButton_Click(object sender, EventArgs e)
        {
            Forms.Defaults defaults = new Forms.Defaults();
            defaults.ShowDialog();
            UpdateField();
        }

        private void MainForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == 27 && placingPoint != null)
            {
                placingPoint = null;
                ControlPointTable.Rows.RemoveAt(ControlPointTable.RowCount - 1);
                UpdateField();
            }
        }

        private void radioRed_CheckedChanged(object sender, EventArgs e)
        {
            if (!noSelectedProfile()) selectedProfile.isRed = radioRed.Checked;
            setBackground(false, false);
        }

        private void radioBlue_CheckedChanged(object sender, EventArgs e)
        {
            if (!noSelectedProfile()) selectedProfile.isRed = !radioBlue.Checked;
            setBackground(true, false);
        }

        private void duplicateProfileButton_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile()) return;

            profiles.Add(new Profile(selectedProfile));
            profileTable.Rows.Add(profiles.Last().Name, profiles.Last().Edited);
            selectProfile(profiles.Count - 1);
        }

        private void shiftPathButton_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile() || noSelectedPath()) return;

            ShiftPath shiftPath = new ShiftPath(selectedPath, this.selectPath);
            shiftPath.ShowDialog();
        }

        private void previewButton_Click(object sender, EventArgs e)
        {
            if (noPointsInPath()) return;

            Forms.Preview preview = new Forms.Preview(selectedProfile.toTxt().Replace(' ', ' '));
            preview.ShowDialog();
        }

        private void deletePointButton_Click(object sender, EventArgs e)
        {
            if (noSelectedPath() || ControlPointTable.SelectedRows.Count == 0) return;

            if (placingPoint != null)
            {
                placingPoint = null;
                ControlPointTable.Rows.RemoveAt(ControlPointTable.RowCount - 1);
                UpdateField();
                return;
            }
            selectedPath.deleteControlPoint(ControlPointTable.SelectedRows[0].Index);

            if (ControlPointTable.SelectedRows[0].Index == 0 && selectedPath.SnapToPrevious)
                selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) - 1].snapLast(selectedPath.ControlPoints[0]);
            if (ControlPointTable.SelectedRows[0].Index == selectedPath.ControlPoints.Count
                        && selectedPath != selectedProfile.Paths.Last()
                        && selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) + 1].SnapToPrevious)
            {
                selectedProfile.Paths[selectedProfile.Paths.IndexOf(selectedPath) + 1].snap(selectedPath);
            }
            selectPath();
        }

        private void mirrorPathButton_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile() || noSelectedPath()) return;

            MirrorPath mirrorPath = new MirrorPath(selectedProfile, selectedPath, selectPath, fieldWidth);
            mirrorPath.ShowDialog();
        }

        private void infoButton_Click(object sender, EventArgs e)
        {
            AboutBox about = new AboutBox();
            about.ShowDialog();
        }

        private void pathTable_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            var grid = sender as DataGridView;
            var rowIdx = (e.RowIndex + 1).ToString();

            var centerFormat = new StringFormat()
            {
                // right alignment might actually make more sense for numbers
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            var headerBounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, grid.RowHeadersWidth, e.RowBounds.Height);
            e.Graphics.DrawString(rowIdx, pathTable.RowHeadersDefaultCellStyle.Font, SystemBrushes.ControlText, headerBounds, centerFormat);
        }
        private void reverseButton_Click(object sender, EventArgs e)
        {
            if (noSelectedProfile() || noSelectedPath()) return;

            selectedPath.reverse();
            selectPath();
        }

        private void setSplineMode(bool isSpline)
        {
            radioLine.Checked = !isSpline;
            radioSpline.Checked = isSpline;
        }

        private void setAllianceMode(bool isRed)
        {
            radioRed.Checked = isRed;
            radioBlue.Checked = !isRed;
        }

        private void radioLine_CheckedChanged(object sender, EventArgs e)
        {
            if (!skipUpdate)
            {
                splineMode = false;
                if (!noSelectedPath()) selectedPath.IsSpline = splineMode;
                UpdateField();
            }
        }

        private void radioSpline_CheckedChanged(object sender, EventArgs e)
        {
            if (!skipUpdate)
            {
                splineMode = true;
                if (!noSelectedPath()) selectedPath.IsSpline = splineMode;
                UpdateField();
            }
        }

        private void MotionProfiler_FormClosed(object sender, FormClosedEventArgs e)
        {
            this.menu.Close();
        }

        private void resetTrackBar()
        {
            //trackBar.Value = 0;
            trackBar_ValueChanged(null, null);
            stopTimer();
        }

        private void trackBar_ValueChanged(object sender, EventArgs e)
        {

            if (noSelectedPath()) return;

            double percent = (double)trackBar.Value / (double)trackBar.Maximum;

            if (selectedPath.gen == null)
            {
                return;
            }

            if(selectedPath.ControlPoints.Count<2)
            {
                return;
            }

            double time = selectedPath.gen.getDuration() * percent;


            State s = selectedPath.gen.calculate(time);

            PState ps = s.getPathState();

            double x = ps.getPose2d().getX();
            double y = ps.getPose2d().getY();

            Rotation2d rot = Rotation2d.fromDegrees(0).minus(ps.getPose2d().getRotation());

            int seriesIndex1 = mainField.Series.IndexOf("robotOutline");
            if (seriesIndex1 != -1) mainField.Series.RemoveAt(seriesIndex1);
            mainField.Series.Add("robotOutline");
            mainField.Series["robotOutline"].ChartType = SeriesChartType.Line;
            mainField.Series["robotOutline"].BorderWidth = 2;
            mainField.Series["robotOutline"].Color = Color.GreenYellow;


            int seriesIndex2 = mainField.Series.IndexOf("robotOutlineAngleMark");
            if (seriesIndex2 != -1) mainField.Series.RemoveAt(seriesIndex2);
            mainField.Series.Add("robotOutlineAngleMark");
            mainField.Series["robotOutlineAngleMark"].ChartType = SeriesChartType.Line;
            mainField.Series["robotOutlineAngleMark"].BorderWidth = 3;
            mainField.Series["robotOutlineAngleMark"].Color = Color.Blue;

            Translation2d fl = new Translation2d(-Properties.Settings.Default.FrameLength / 2, Properties.Settings.Default.FrameWidth / 2).rotateBy(rot).plus(new Translation2d(x, y));
            Translation2d fr = new Translation2d(Properties.Settings.Default.FrameLength / 2, Properties.Settings.Default.FrameWidth / 2).rotateBy(rot).plus(new Translation2d(x, y));
            Translation2d br = new Translation2d(Properties.Settings.Default.FrameLength / 2, -Properties.Settings.Default.FrameWidth / 2).rotateBy(rot).plus(new Translation2d(x, y));
            Translation2d bl = new Translation2d(-Properties.Settings.Default.FrameLength / 2, -Properties.Settings.Default.FrameWidth / 2).rotateBy(rot).plus(new Translation2d(x, y));

            Translation2d CenterMark = new Translation2d(0, Properties.Settings.Default.FrameWidth / 2).rotateBy(rot).plus(new Translation2d(x, y));
            Translation2d CenterMark2 = new Translation2d(0, Properties.Settings.Default.FrameWidth / 2 + .25).rotateBy(rot).plus(new Translation2d(x, y));

            mainField.Series["robotOutline"].Points.AddXY(fl.getX(), fl.getY());
            mainField.Series["robotOutline"].Points.AddXY(fr.getX(), fr.getY());
            mainField.Series["robotOutline"].Points.AddXY(br.getX(), br.getY());
            mainField.Series["robotOutline"].Points.AddXY(bl.getX(), bl.getY());
            mainField.Series["robotOutline"].Points.AddXY(fl.getX(), fl.getY());

            mainField.Series["robotOutlineAngleMark"].Points.AddXY(CenterMark.getX(), CenterMark.getY());
            mainField.Series["robotOutlineAngleMark"].Points.AddXY(CenterMark2.getX(), CenterMark2.getY());

        }
        DateTime startTime = DateTime.Now;
        private void timer1_Tick(object sender, EventArgs e)
        {

            if (noSelectedPath() || selectedPath.gen == null)
            {
                stopTimer();
                return;
            }

            TimeSpan time = DateTime.Now - startTime;
            if (time.TotalSeconds + timeOffset > selectedPath.gen.getDuration())
            {
                trackBar.Value = trackBar.Maximum;
                stopTimer();
                return;
            }
            int trackbarvalue = (int)(((time.TotalSeconds + timeOffset) / selectedPath.gen.getDuration()) * trackBar.Maximum);

            if (trackbarvalue <= trackBar.Maximum && trackbarvalue >= trackBar.Minimum)
                trackBar.Value = trackbarvalue;
            trackBar_ValueChanged(null, null);

        }
        private void stopTimer()
        {
            timer1.Stop();
            playButton.IconChar = FontAwesome.Sharp.IconChar.Play;
        }

        private double timeOffset = 0.0;

        private void startTimer()
        {
            timeOffset = 0.0;
            timer1.Start();
            playButton.IconChar = FontAwesome.Sharp.IconChar.Pause;
            double percent = (double)trackBar.Value / (double)trackBar.Maximum;

            if (selectedPath.gen == null) return;

            if (percent != 1.0) timeOffset = selectedPath.gen.getDuration() * percent;
            
        }
        private void playButton_Click(object sender, EventArgs e)
        {
            if (noSelectedPath() || selectedPath.gen == null)
            {
                stopTimer();
                return;
            }

            if (playButton.IconChar == FontAwesome.Sharp.IconChar.Pause)
            {
                stopTimer();
                return;
            }

            startTime = DateTime.Now;
            startTimer();
        }

        private void iconButton2_Click(object sender, EventArgs e)
        {
            menu.constants.Show();
            this.Hide();
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
                TimeSpan ts = DateTime.Now - timeOfUpload;


                timeSinceUpload.Text = "Last Upload: " + ts.ToString("h'h 'm'm 's's'");
        }

        private void MotionProfiler_Resize(object sender, EventArgs e)
        {
            double hw = 679.0 / 638.0;
            double wh = 638.0 / 679.0;
            if (panel1.Width<=panel1.Height)
            {

                mainField.Width = (int)(panel1.Width);

                mainField.Height = (int)(panel1.Width* wh);

                mainField.Location = new Point(panel1.Location.X + (int)(panel1.Width / 2.0) - mainField.Width/2, panel1.Location.Y + (int)(panel1.Height / 2.0) - mainField.Height/2-50);


            }
            if (panel1.Height <= panel1.Width)
            {

                mainField.Height = (int)(panel1.Height);

                mainField.Width = (int)(panel1.Height * hw);

                mainField.Location = new Point(panel1.Location.X + (int)(panel1.Width / 2.0) - mainField.Width/2, panel1.Location.Y + (int)(panel1.Height / 2.0)- mainField.Height/2-50);



            }
        }

        public static void saveUndoState(bool clearRedo = true, List<Profile> profiles = null)
        {
            if(clearRedo)
                redo.Clear();

            if(profiles == null)
            {
                profiles = MotionProfiler.profiles;
                List<Profile> ps = new List<Profile>();
                foreach (Profile p in profiles)
                {
                    ps.Add(new Profile(p));
                }
                undo.Add(ps);
            }
            else
            {
                List<Profile> ps = new List<Profile>();
                foreach (Profile p in profiles)
                {
                    ps.Add(new Profile(p));
                }
                undo.Add(ps);
            }
            
            Console.WriteLine("SAVE");
        }

        public static void saveRedoState()
        {
            List<Profile> ps = new List<Profile>();
            foreach (Profile p in profiles)
            {
                ps.Add(new Profile(p));
            }
            redo.Add(ps);
            Console.WriteLine("SAVE REDO");
        }

        private void undoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            placingPoint = null;
            if (undo.Count>0)
            {
                skipUpdate = true;
                saveRedoState();

                int selectedProfileIndex = -1;
                int selectedPathIndex = -1;

                if (pathTable.SelectedRows.Count > 0)
                {
                    selectedPathIndex = pathTable.SelectedCells[0].RowIndex;
                    pathTable.ClearSelection();
                }

                if (profileTable.SelectedCells.Count > 0)
                {
                    selectedProfileIndex = profileTable.SelectedCells[0].RowIndex;
                    profileTable.ClearSelection();
                }
                profileTable.Rows.Clear();

                profiles = undo.Last();

                undo.Remove(undo.Last());

                foreach (Profile p in profiles)
                {
                    profileTable.Rows.Add(p.Name, p.Edited);
                }

                
                if (profileTable.Rows.Count == 0)
                {
                    selectProfile();
                }
                else if (profileTable.Rows.Count - 1 >= selectedProfileIndex)
                {
                    selectProfile(selectedProfileIndex);
                }
                else if(profileTable.Rows.Count>=1)
                {
                    selectProfile(profileTable.Rows.Count - 1);
                }



                if (pathTable.Rows.Count == 0)
                {
                    selectPath();
                }
                else if (pathTable.Rows.Count - 1 >= selectedPathIndex)
                {
                    selectPath(selectedPathIndex);
                }
                else if (pathTable.Rows.Count >= 1)
                {
                    selectPath(pathTable.Rows.Count - 1);
                }

                skipUpdate = false;
                UpdateField();
            }
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            placingPoint = null;
            if (redo.Count > 0)
            {
                skipUpdate = true;
                saveUndoState(false);
                int selectedIndex = -1;
                if (profileTable.SelectedCells.Count > 0)
                {
                    selectedIndex = profileTable.SelectedCells[0].RowIndex;
                    profileTable.ClearSelection();
                }

                int selectedPathIndex = -1;

                if (pathTable.SelectedRows.Count > 0)
                {
                    selectedPathIndex = pathTable.SelectedCells[0].RowIndex;
                    pathTable.ClearSelection();
                }

                profileTable.Rows.Clear();

                profiles = redo.Last();

                redo.Remove(redo.Last());


                foreach (Profile p in profiles)
                {
                    profileTable.Rows.Add(p.Name, p.Edited);
                }

                
                if (profileTable.Rows.Count == 0)
                {
                    selectProfile();
                }
                else if (profileTable.Rows.Count - 1 >= selectedIndex)
                {
                    selectProfile(selectedIndex);
                }
                else if (profileTable.Rows.Count >= 1)
                {
                    selectProfile(profileTable.Rows.Count - 1);
                }

                if (pathTable.Rows.Count == 0)
                {
                    selectPath();
                }
                else if (pathTable.Rows.Count - 1 >= selectedPathIndex)
                {
                    selectPath(selectedPathIndex);
                }
                else if (pathTable.Rows.Count >= 1)
                {
                    selectPath(pathTable.Rows.Count - 1);
                }

                skipUpdate = false;
                UpdateField();
            }
        }


    }
}
