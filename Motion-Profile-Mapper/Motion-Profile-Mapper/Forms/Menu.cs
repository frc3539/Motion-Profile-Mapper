﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MotionProfileMapper.Forms
{
    public partial class Menu : Form
    {
        public MotionProfileMapper.MotionProfiler mp;
        public ConfigurationView constants;


        public Menu()
        {
            mp = new MotionProfileMapper.MotionProfiler(this);
            constants = new ConfigurationView(this);

            InitializeComponent();
        }

        private void mpButton_Click(object sender, EventArgs e)
        {
            mp.Show();
            this.Hide();
        }

        private void constantsButton_Click(object sender, EventArgs e)
        {
            constants.Show();
            this.Hide();
        }
    }
}