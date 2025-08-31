#!/usr/bin/env python3
"""
Script to convert all PNG files in a folder to a single PDF document.
Each PNG image becomes one page in the PDF.
"""

import os
import glob
from PIL import Image
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import letter, A4
from reportlab.lib.utils import ImageReader
import sys

def png_to_pdf(folder_path, output_filename="converted_images.pdf"):
    """
    Convert all PNG files in a folder to a single PDF.
    
    Args:
        folder_path (str): Path to the folder containing PNG files
        output_filename (str): Name of the output PDF file
    """
    
    # Check if folder exists
    if not os.path.exists(folder_path):
        print(f"Error: Folder '{folder_path}' does not exist.")
        return False
    
    # Get all PNG files in the folder
    png_pattern = os.path.join(folder_path, "*.png")
    png_files = glob.glob(png_pattern)
    
    if not png_files:
        print(f"No PNG files found in '{folder_path}'")
        return False
    
    # Sort files alphabetically for consistent ordering
    png_files.sort()
    
    print(f"Found {len(png_files)} PNG files:")
    for file in png_files:
        print(f"  - {os.path.basename(file)}")
    
    # Create output PDF path
    output_path = os.path.join(folder_path, output_filename)
    
    # Create PDF
    try:
        c = canvas.Canvas(output_path, pagesize=letter)
        page_width, page_height = letter
        
        for i, png_file in enumerate(png_files):
            print(f"Processing {os.path.basename(png_file)} ({i+1}/{len(png_files)})")
            
            try:
                # Open image to get dimensions
                with Image.open(png_file) as img:
                    img_width, img_height = img.size
                
                # Calculate scaling to fit image on page while maintaining aspect ratio
                # Leave some margin (50 points on each side)
                margin = 50
                available_width = page_width - (2 * margin)
                available_height = page_height - (2 * margin)
                
                # Calculate scale factors
                width_scale = available_width / img_width
                height_scale = available_height / img_height
                scale = min(width_scale, height_scale)
                
                # Calculate new dimensions
                new_width = img_width * scale
                new_height = img_height * scale
                
                # Center the image on the page
                x = (page_width - new_width) / 2
                y = (page_height - new_height) / 2
                
                # Add image to PDF
                c.drawImage(png_file, x, y, width=new_width, height=new_height)
                
                # Start new page for next image (except for the last image)
                if i < len(png_files) - 1:
                    c.showPage()
                    
            except Exception as e:
                print(f"Warning: Could not process {png_file}: {e}")
                continue
        
        # Save the PDF
        c.save()
        print(f"\nPDF created successfully: {output_path}")
        return True
        
    except Exception as e:
        print(f"Error creating PDF: {e}")
        return False

def main():
    # Folder path - modify this to your desired folder
    folder_path = "/Users/charliebaker/Desktop/NEWIMGS"
    
    # Optional: customize output filename
    output_filename = "converted_images.pdf"
    
    print("PNG to PDF Converter")
    print("===================")
    print(f"Source folder: {folder_path}")
    print(f"Output file: {output_filename}")
    print()
    
    success = png_to_pdf(folder_path, output_filename)
    
    if success:
        print("\nConversion completed successfully!")
    else:
        print("\nConversion failed. Please check the error messages above.")
        sys.exit(1)

if __name__ == "__main__":
    main()